using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Rpc;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain state storage model
/// Contains Agent metadata (excluding business state), automatically persisted by Orleans GrainStorage
/// 
/// Note: Business state (Snapshot) is now handled by IStateStore&lt;TState&gt;,
/// stored in MongoDB collections partitioned by type (agent_states_{StateType})
/// </summary>
[GenerateSerializer]
public class OrleansAgentState
{
    /// <summary>
    /// Agent type name (assembly-qualified name)
    /// </summary>
    [Id(0)]
    public string? AgentTypeName { get; set; }

    /// <summary>
    /// Agent's unique identifier
    /// </summary>
    [Id(1)]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Parent node ID
    /// </summary>
    [Id(2)]
    public string? ParentId { get; set; }

    /// <summary>
    /// Child node ID list
    /// </summary>
    [Id(3)]
    public List<string> Children { get; set; } = new();

    public OrleansAgentState() { }

    public OrleansAgentState(string? parentId = null)
    {
        ParentId = parentId;
        Children = new List<string>();
    }
}

/// <summary>
/// Orleans GAgent Grain - Agent business logic executes within Silo
/// 
/// Responsibilities:
/// 1. Create and hold Agent instances within Silo
/// 2. Execute Agent business logic within Silo (HandleEventAsync)
/// 3. Store hierarchy relationships (Parent/Children)
/// 4. Manage Orleans Streams subscriptions
/// </summary>
[Reentrant]
public class OrleansGAgentGrain : Grain, IGAgentGrain
{
    // Grain persistent state
    private readonly IPersistentState<OrleansAgentState> _grainState;

    // Agent instance - created and executed within Silo
    private IGAgent? _agent;
    private bool _isInitialized;

    // Unified Message Stream (can be Orleans Stream or MassTransit Stream)
    private IMessageStream? _myStream;
    private IMessageStreamSubscription? _streamSubscription;

    // Stream Factory - unified management of Stream creation logic
    private OrleansStreamFactory? _streamFactory;

    // Logger
    private ILogger<OrleansGAgentGrain> _logger = NullLogger<OrleansGAgentGrain>.Instance;

    public OrleansGAgentGrain(
        [PersistentState("agentState")]
        IPersistentState<OrleansAgentState> grainState)
    {
        _grainState = grainState;
    }

    #region Grain Lifecycle

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger = ServiceProvider.GetService<ILogger<OrleansGAgentGrain>>()
                  ?? NullLogger<OrleansGAgentGrain>.Instance;

        _logger.LogInformation("🚀 Activating OrleansGAgentGrain {GrainId}", this.GetGrainId());

        // Initialize Stream Factory - unified management of Orleans/MassTransit Stream selection logic
        // Factory will get IMessageStreamProvider (MassTransit) and configuration from DI
        _streamFactory = ServiceProvider.GetService<OrleansStreamFactory>()
            ?? ActivatorUtilities.CreateInstance<OrleansStreamFactory>(ServiceProvider);

        // Initialize Stream (select Orleans Stream or MassTransit based on configuration)
        await InitializeStreamAsync();

        // Restore Agent if previously initialized
        if (!string.IsNullOrEmpty(_grainState.State.AgentTypeName) && !string.IsNullOrEmpty(_grainState.State.AgentId))
        {
            _logger.LogInformation("Restoring Agent from persisted state: {AgentType}, AgentId: {AgentId}", 
                _grainState.State.AgentTypeName, _grainState.State.AgentId);
            await InitializeAgentInternalAsync(_grainState.State.AgentTypeName, _grainState.State.AgentId);
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deactivating OrleansGAgentGrain {GrainId}", this.GetGrainId());

        // Deactivate Agent
        if (_agent != null)
        {
            try
            {
                await _agent.DeactivateAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deactivating Agent in Grain {GrainId}", this.GetGrainId());
            }
        }

        // Unsubscribe from unified Stream
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
                _streamSubscription = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from stream for Grain {GrainId}", this.GetGrainId());
            }
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <summary>
    /// Initialize Stream - select Orleans Stream or MassTransit Stream based on configuration
    /// Maintains consistent configuration-driven pattern with LocalGAgentActor
    /// 
    /// StreamId format: Full GrainKey (AgentTypeShortName:AgentId)
    /// This ensures consistency with Client Actor stream addressing.
    /// </summary>
    private async Task InitializeStreamAsync()
    {
        try
        {
            if (_streamFactory == null)
            {
                _logger.LogWarning("StreamFactory not initialized for Grain {GrainId}", this.GetGrainId());
                return;
            }

            // Use full GrainKey as StreamId for consistency with Client Actor
            var grainKey = this.GetPrimaryKeyString();

            // Use Factory to create Stream uniformly (automatically selects Orleans/MassTransit)
            // Use full GrainKey (AgentType:AgentId) as streamId
            _myStream = await _streamFactory.CreateStreamAsync(
                grainKey,  // Full GrainKey format: AgentTypeShortName:AgentId
                null,
                this.GetStreamProvider);

            var providerType = _streamFactory.DetermineProviderType();
            _logger.LogInformation("📡 Using {ProviderType} Stream for Grain {GrainId}, StreamKey={StreamKey}",
                providerType, this.GetGrainId(), grainKey);

            // Subscribe to stream for external event handling
            if (_myStream != null)
            {
                _streamSubscription = await _myStream.SubscribeAsync<EventEnvelope>(
                    OnStreamEventReceivedAsync,
                    null,
                    CancellationToken.None);
                _logger.LogDebug("Successfully subscribed to stream for Grain {GrainId}", this.GetGrainId());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize streams for Grain {GrainId}", this.GetGrainId());
        }
    }
    #endregion

    #region Stream-based Message Sending

    /// <summary>
    /// Send event to target Agent via Stream (non-blocking, async)
    /// 
    /// Advantages (compared to RPC):
    /// 1. Non-blocking: Returns immediately after sending, doesn't wait for target processing
    /// 2. Order guarantee: Message order is guaranteed within Kafka partitions
    /// 3. Decoupling: Sender and receiver are completely decoupled
    /// 4. Resilience: Messages are cached in queue when receiver is unavailable
    /// </summary>
    private async Task SendEventToStreamAsync(string targetAgentId, EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var targetStream = GetTargetStream(targetAgentId);
            if (targetStream == null)
            {
                _logger.LogWarning("Cannot get stream for target Agent {TargetId}", targetAgentId);
                return;
            }

            _logger.LogDebug("📤 Sending event {EventId} to Agent {TargetId} via Stream", 
                envelope.Id, targetAgentId);
            await targetStream.ProduceAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send event {EventId} to Agent {TargetId} via Stream", 
                envelope.Id, targetAgentId);
        }
    }

    /// <summary>
    /// Get target Agent's stream
    /// Use Factory to create uniformly (automatically selects MassTransit or Orleans Stream)
    /// </summary>
    private IMessageStream? GetTargetStream(string targetAgentId)
    {
        if (_streamFactory == null)
        {
            _logger.LogWarning("StreamFactory not initialized, cannot get target stream for Agent {TargetId}", targetAgentId);
            return null;
        }

        try
        {
            // Extract category from targetAgentId (format: AgentTypeShortName:AgentId)
            // Category is needed for MassTransit TopicMapping (e.g., "TypeAAgent" -> "AevatarAgents-TypeA")
            string? agentCategory = null;
            var colonIndex = targetAgentId.IndexOf(':');
            if (colonIndex > 0)
            {
                agentCategory = targetAgentId.Substring(0, colonIndex);
            }
            
            // Use Factory to create target Agent's Stream (synchronous get, because Factory.CreateStreamAsync is essentially synchronous)
            return _streamFactory.CreateStreamAsync(targetAgentId, agentCategory, this.GetStreamProvider).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stream for target Agent {TargetId}", targetAgentId);
            return null;
        }
    }

    /// <summary>
    /// Propagate event to Parent/Children based on Direction via Stream (non-blocking)
    /// 
    /// All propagation is done through Stream, no RPC used, avoiding blocking
    /// </summary>
    private async Task PropagateEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.Direction)
        {
            case EventDirection.Down:
                await SendToChildrenViaStreamAsync(envelope, ct);
                break;
            case EventDirection.Up:
                await SendToParentViaStreamAsync(envelope, ct);
                break;
            case EventDirection.Both:
                // Send in parallel, don't wait
                var downTask = SendToChildrenViaStreamAsync(envelope, ct);
                var upTask = SendToParentViaStreamAsync(envelope, ct);
                await Task.WhenAll(downTask, upTask);
                break;
        }
    }

    /// <summary>
    /// Send event to all children via Stream (parallel, non-blocking)
    /// </summary>
    private async Task SendToChildrenViaStreamAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var children = _grainState.State.Children;
        if (children.Count == 0) return;

        _logger.LogDebug("📤 Broadcasting event {EventId} to {ChildCount} children via Stream", 
            envelope.Id, children.Count);
        var tasks = children.Select(childId => SendEventToStreamAsync(childId, envelope, ct));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Send event to parent via Stream (non-blocking)
    /// </summary>
    private async Task SendToParentViaStreamAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var parentId = _grainState.State.ParentId;
        if (string.IsNullOrEmpty(parentId)) return;

        _logger.LogDebug("📤 Sending event {EventId} to parent {ParentId} via Stream", 
            envelope.Id, parentId);
        await SendEventToStreamAsync(parentId, envelope, ct);
    }

    #endregion

    #region Agent Initialization

    public Task<bool> InitializeAgentAsync(string agentTypeName)
    {
        // ============================================================
        //  AgentId unified specification (aligned with docs/AGENT_ID_GUIDE.md)
        //
        //  Orleans GrainKey / StreamKey uniformly use full ActorId:
        //    "AgentTypeShortName:RawId"
        //
        //  WHY:
        //  - Using only RawId causes cross-type conflicts (same RawId with different AgentType would reuse the same Grain)
        //  - PublisherId / self-handling / StreamKey must be consistent, otherwise there will be hidden bugs like "self-published events cannot be recognized as self"
        // ============================================================
        var grainKey = this.GetPrimaryKeyString();
        var actorId = NormalizeActorId(agentTypeName, grainKey);
        return InitializeAgentInternalAsync(agentTypeName, actorId, persistState: true);
    }

    private async Task<bool> InitializeAgentInternalAsync(string agentTypeName, string agentId, bool persistState = false)
    {
        if (_isInitialized && _agent != null)
        {
            return true;
        }

        try
        {
            _logger.LogInformation("🔧 Initializing Agent in Grain {GrainId}, Type: {AgentType}", 
                this.GetGrainId(), agentTypeName);

            // Ensure agentId is in normalized ActorId format for consistent self-handling and stream routing.
            agentId = NormalizeActorId(agentTypeName, agentId);

            // Resolve Agent type
            var agentType = ResolveAgentType(agentTypeName);
            if (agentType == null)
            {
                _logger.LogError("Failed to resolve Agent type: {AgentType}", agentTypeName);
                return false;
            }

            // Create Agent instance using DI
            _agent = CreateAgentInstance(agentType, agentId);
            if (_agent == null)
            {
                _logger.LogError("Failed to create Agent instance: {AgentType}", agentTypeName);
                return false;
            }

            // Inject dependencies
            InjectAgentDependencies(_agent);

            // Activate Agent
            await _agent.ActivateAsync(CancellationToken.None);

            _isInitialized = true;

            // Persist Agent info for recovery
            if (persistState)
            {
                _grainState.State.AgentTypeName = agentTypeName;
                _grainState.State.AgentId = agentId;
                await _grainState.WriteStateAsync();
            }

            _logger.LogInformation("✅ Agent initialized successfully in Grain {GrainId}, Type: {AgentType}", 
                this.GetGrainId(), agentType.Name);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Agent in Grain {GrainId}", this.GetGrainId());
            return false;
        }
    }

    private static string NormalizeActorId(string agentTypeName, string idOrActorId)
    {
        if (string.IsNullOrWhiteSpace(idOrActorId))
            return string.Empty;

        var id = idOrActorId.Trim();
        if (id.Contains(AgentId.Separator))
            return id;

        var shortName = AgentId.GetAgentTypeShortName(agentTypeName);
        return string.IsNullOrWhiteSpace(shortName)
            ? id
            : $"{shortName}{AgentId.Separator}{id}";
    }

    private System.Type? ResolveAgentType(string agentTypeName)
    {
        // Try direct type resolution
        var type = System.Type.GetType(agentTypeName);
        if (type != null) return type;

        // Search all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(agentTypeName);
                if (type != null) return type;

                // Try by simple name
                type = assembly.GetTypes()
                    .FirstOrDefault(t => t.FullName == agentTypeName || t.Name == agentTypeName);
                if (type != null) return type;
            }
            catch
            {
                // Ignore assembly loading errors
            }
        }

        return null;
    }

    private IGAgent? CreateAgentInstance(System.Type agentType, string agentId)
    {
        try
        {
            // Try DI first
            var agent = ServiceProvider.GetService(agentType) as IGAgent;
            if (agent != null)
            {
                // Set ID via reflection
                SetAgentId(agent, agentId);
                return agent;
            }

            // Fallback: Create using ActivatorUtilities
            agent = ActivatorUtilities.CreateInstance(ServiceProvider, agentType) as IGAgent;
            if (agent != null)
            {
                SetAgentId(agent, agentId);
                return agent;
            }

            // Last resort: direct instantiation
            agent = Activator.CreateInstance(agentType) as IGAgent;
            if (agent != null)
            {
                SetAgentId(agent, agentId);
            }
            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Agent instance: {AgentType}", agentType.Name);
            return null;
        }
    }

    private void SetAgentId(IGAgent agent, string id)
    {
        // Find Id property in base class hierarchy
        var type = agent.GetType();
        while (type != null)
        {
            var idProperty = type.GetProperty("Id", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.Public | 
                System.Reflection.BindingFlags.NonPublic);
            
            if (idProperty != null && idProperty.CanWrite)
            {
                idProperty.SetValue(agent, id);
                return;
            }

            // Try field
            var idField = type.GetField("_id", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
            if (idField != null)
            {
                idField.SetValue(agent, id);
                return;
            }

            type = type.BaseType;
        }
    }

    private void InjectAgentDependencies(IGAgent agent)
    {
        // Inject Logger
        LoggerInjector.InjectLogger(agent, ServiceProvider);

        // Inject StateProjector
        StateProjectorInjector.InjectStateProjector(agent, ServiceProvider);

        // Inject StateStore (for snapshots in EventSourcing mode, or simple state persistence)
        // IMPORTANT: Must inject StateStore BEFORE EventStore, as EventSourcing needs StateStore for snapshots
        AgentStateStoreInjector.InjectStateStore(agent, ServiceProvider);

        // Inject EventStore (only if agent supports EventSourcing)
        if (AgentEventStoreInjector.HasEventStore(agent))
        {
            AgentEventStoreInjector.InjectEventStore(agent, ServiceProvider);
        }

        // Inject EventPublisher (Grain acts as the publisher)
        // Supports broadcast propagation and point-to-point sending (all through Stream, non-blocking)
        AgentEventPublisherInjector.InjectEventPublisher(agent, new GrainEventPublisher(this, _myStream, _streamFactory, _logger));

        // Inject ActorFactory (for Agents that need to create child Agents)
        InjectActorFactory(agent);

        // Inject Configuration Options (preferred over ServiceProvider)
        InjectConfigurationOptions(agent);

        // Inject AgentContextAccessor for context propagation in event handlers
        var contextAccessor = ServiceProvider.GetService<IAgentContextAccessor>();
        if (contextAccessor != null)
        {
            AgentContextAccessorInjector.InjectContextAccessor(agent, contextAccessor);
        }
        // Inject AI-related dependencies (LLMProviderFactory, EmbeddingFactory)
        // Only inject if agent is an AI Agent (inherits from AIGAgentBase)
        if (AIAgentLLMProviderFactoryInjector.HasLLMProviderFactory(agent))
        {
            AIAgentLLMProviderFactoryInjector.InjectLLMProviderFactory(agent, ServiceProvider);
            _logger.LogDebug("✅ Injected LLMProviderFactory into AI Agent {AgentType}", agent.GetType().Name);
        }

        if (AIAgentEmbeddingFactoryInjector.HasEmbeddingFactory(agent))
        {
            AIAgentEmbeddingFactoryInjector.InjectEmbeddingFactory(agent, ServiceProvider);
            _logger.LogDebug("✅ Injected EmbeddingFactory into AI Agent {AgentType}", agent.GetType().Name);
        }

        // Inject ToolManager for AI Agents with Tool support
        AIAgentToolManagerInjector.InjectToolManager(agent, ServiceProvider);
        
        // Inject ServiceProvider for agents that need direct service resolution (e.g., for IStreamProviderManager)
        // This is specifically for agents like GodChatGAgent that need to publish to Orleans Streams directly
        InjectServiceProviderProperty(agent);
    }
    
    /// <summary>
    /// Inject ServiceProvider into agents that have a public settable ServiceProvider property.
    /// This is a targeted injection for agents that need direct access to IStreamProviderManager or other runtime services.
    /// </summary>
    private void InjectServiceProviderProperty(IGAgent agent)
    {
        var agentType = agent.GetType();
        var serviceProviderProperty = agentType.GetProperty(
            "ServiceProvider", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        if (serviceProviderProperty != null && 
            serviceProviderProperty.CanWrite &&
            serviceProviderProperty.PropertyType == typeof(IServiceProvider))
        {
            serviceProviderProperty.SetValue(agent, ServiceProvider);
            _logger.LogDebug("✅ Injected ServiceProvider into Agent {AgentType}", agentType.Name);
        }
    }
    
    /// <summary>
    /// Inject configuration options into Agent properties.
    /// Supports:
    /// 1. IOptionsMonitor&lt;T&gt; properties - inject the IOptionsMonitor directly
    /// 2. IOptions&lt;T&gt; properties - inject the IOptions directly  
    /// 3. T properties (where T is an options class) - inject the CurrentValue/Value
    /// </summary>
    private void InjectConfigurationOptions(IGAgent agent)
    {
        var agentType = agent.GetType();
        var properties = agentType.GetProperties(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        
        _logger.LogInformation("🔍 InjectConfigurationOptions scanning {PropertyCount} properties in {AgentType}", 
            properties.Length, agentType.Name);
        
        foreach (var property in properties)
        {
            if (!property.CanWrite) continue;
            
            var propertyType = property.PropertyType;
            
            // Skip common non-option types
            if (propertyType == typeof(string) || propertyType.IsPrimitive)
                continue;
            
            // Case 1: Property is IOptionsMonitor<T> - inject the IOptionsMonitor directly
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IOptionsMonitor<>))
            {
                var service = ServiceProvider.GetService(propertyType);
                var innerType = propertyType.GetGenericArguments()[0];
                if (service != null)
                {
                    property.SetValue(agent, service);
                    _logger.LogInformation("✅ Injected IOptionsMonitor<{OptionType}> into {AgentType}.{Property}", 
                        innerType.Name, agentType.Name, property.Name);
                }
                else
                {
                    _logger.LogWarning("⚠️ IOptionsMonitor<{OptionType}> not found in DI for {AgentType}.{Property}",
                        innerType.Name, agentType.Name, property.Name);
                }
                continue;
            }
            
            // Case 2: Property is IOptions<T> - inject the IOptions directly
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IOptions<>))
            {
                var service = ServiceProvider.GetService(propertyType);
                if (service != null)
                {
                    property.SetValue(agent, service);
                    var innerType = propertyType.GetGenericArguments()[0];
                    _logger.LogDebug("✅ Injected IOptions<{OptionType}> into {AgentType}.{Property}", 
                        innerType.Name, agentType.Name, property.Name);
                }
                continue;
            }
            
            // Skip other interface types
            if (propertyType.IsInterface)
                continue;
                
            // Case 3: Property is a concrete options class T - inject CurrentValue from IOptionsMonitor<T> or Value from IOptions<T>
            // Try to get IOptionsMonitor<T> first
            var optionsMonitorType = typeof(IOptionsMonitor<>).MakeGenericType(propertyType);
            var optionsMonitor = ServiceProvider.GetService(optionsMonitorType);
            if (optionsMonitor != null)
            {
                // Get CurrentValue from IOptionsMonitor<T>
                var currentValueProp = optionsMonitorType.GetProperty("CurrentValue");
                if (currentValueProp != null)
                {
                    var value = currentValueProp.GetValue(optionsMonitor);
                    if (value != null)
                    {
                        property.SetValue(agent, value);
                        _logger.LogDebug("✅ Injected {OptionType} (from IOptionsMonitor) into {AgentType}.{Property}", 
                            propertyType.Name, agentType.Name, property.Name);
                    }
                }
                continue;
            }
            
            // Try to get IOptions<T>
            var optionsType = typeof(IOptions<>).MakeGenericType(propertyType);
            var options = ServiceProvider.GetService(optionsType);
            if (options != null)
            {
                var valueProp = optionsType.GetProperty("Value");
                if (valueProp != null)
                {
                    var value = valueProp.GetValue(options);
                    if (value != null)
                    {
                        property.SetValue(agent, value);
                        _logger.LogDebug("✅ Injected {OptionType} (from IOptions) into {AgentType}.{Property}", 
                            propertyType.Name, agentType.Name, property.Name);
                    }
                }
            }
        }
    }

    private void InjectActorFactory(IGAgent agent)
    {
        // Find ActorFactory property via reflection
        var agentType = agent.GetType();
        var actorFactoryProperty = agentType.GetProperty("ActorFactory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        if (actorFactoryProperty == null || !actorFactoryProperty.CanWrite)
            return;

        // Get IGAgentActorFactory from DI
        var actorFactory = ServiceProvider.GetService<IGAgentActorFactory>();
        if (actorFactory == null)
        {
            _logger.LogWarning("IGAgentActorFactory not registered in DI, cannot inject into Agent {AgentType}", agentType.Name);
            return;
        }

        actorFactoryProperty.SetValue(agent, actorFactory);
    }

    public Task<bool> IsInitializedAsync()
    {
        return Task.FromResult(_isInitialized && _agent != null);
    }

    public async Task<string> GetDescriptionAsync()
    {
        if (_agent == null)
        {
            return $"Uninitialized Grain {this.GetPrimaryKeyString()}";
        }

        return await _agent.GetDescriptionAsync();
    }

    #endregion

    #region Event Handling

    /// <summary>
    /// Handle event - Execute business logic in Silo and route based on Direction
    /// 
    /// Event flow processing:
    /// 1. Agent processes event (single call point)
    /// 2. Propagate to Parent/Children based on Direction (via Stream, non-blocking)
    ///    - Send Down only if there are child nodes
    ///    - Send Up only if there is a parent node
    /// </summary>
    public async Task HandleEventAsync(byte[] envelopeBytes)
    {
        if (envelopeBytes == null || envelopeBytes.Length == 0)
        {
            _logger.LogWarning("Received empty event bytes in Grain {GrainId}", this.GetGrainId());
            return;
        }

        if (_agent == null)
        {
            _logger.LogWarning("Agent not initialized in Grain {GrainId}, cannot handle event", this.GetGrainId());
            return;
        }

        try
        {
            var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
            _logger.LogInformation("🔥 Grain {GrainId} handling event {EventId} with Direction {Direction}", 
                this.GetGrainId(), envelope.Id, envelope.Direction);

            // ✅ Step 1: Execute business logic in Silo (single call point)
            await _agent.HandleEventAsync(envelope, CancellationToken.None);
            
            // ✅ Step 2: Direction-based propagation via Stream (non-blocking)
            // Propagate only when there are subscribers (Parent/Children)
            await PropagateEventAsync(envelope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event in Grain {GrainId}", this.GetGrainId());
            throw;
        }
    }

    /// <summary>
    /// Stream callback - Handle events from Stream (MassTransit/Orleans Stream)
    /// 
    /// Used to receive events from Stream:
    /// 1. Event input from external systems (e.g., MassTransit Kafka)
    /// 2. Events sent by other Agents via Stream
    /// </summary>
    private async Task OnStreamEventReceivedAsync(EventEnvelope envelope)
    {
        if (_agent == null)
        {
            _logger.LogDebug("Agent not initialized, skipping stream event in Grain {GrainId}", this.GetGrainId());
            return;
        }

        try
        {
            // Skip self-published events (avoid duplicate processing)
            var grainKey = this.GetPrimaryKeyString();
            if (envelope.PublisherId == grainKey)
            {
                _logger.LogDebug("Skipping self-published event {EventId} from stream in Grain {GrainId}", 
                    envelope.Id, this.GetGrainId());
                return;
            }
            
            _logger.LogDebug("Grain {GrainId} processing external event {EventId} from stream", 
                this.GetGrainId(), envelope.Id);

            // External event: execute business logic and propagate
            await _agent.HandleEventAsync(envelope, CancellationToken.None);
            
            // If there is Direction, continue propagation
            await PropagateEventAsync(envelope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream event in Grain {GrainId}", this.GetGrainId());
        }
    }

    #endregion

    #region Hierarchy Management

    public Task<string> GetIdAsync()
    {
        var grainKey = this.GetPrimaryKeyString();
        try
        {
            // Align with Actor.Id / StreamId: return full ActorId ("Type:RawId")
            return Task.FromResult(NormalizeActorId(_grainState.State.AgentTypeName ?? string.Empty, grainKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AgentId from Grain key '{GrainKey}'", grainKey);
            return Task.FromResult(string.Empty);
        }
    }

    public async Task AddChildAsync(string childId)
    {
        if (!_grainState.State.Children.Contains(childId))
        {
            _grainState.State.Children.Add(childId);
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Added child {ChildId} to Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task RemoveChildAsync(string childId)
    {
        if (_grainState.State.Children.Remove(childId))
        {
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Removed child {ChildId} from Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task SetParentAsync(string parentId)
    {
        if (_grainState.State.ParentId != parentId)
        {
            _grainState.State.ParentId = parentId;
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Set parent {ParentId} for Grain {GrainId}", parentId, this.GetGrainId());
        }
    }

    public async Task ClearParentAsync()
    {
        if (!string.IsNullOrEmpty(_grainState.State.ParentId))
        {
            _grainState.State.ParentId = null;
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Cleared parent for Grain {GrainId}", this.GetGrainId());
        }
    }

    public Task<IReadOnlyList<string>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(_grainState.State.Children.AsReadOnly());
    }

    public Task<string?> GetParentAsync()
    {
        return Task.FromResult(_grainState.State.ParentId);
    }

    #endregion

    #region Lifecycle Control

    public Task DeactivateAsync()
    {
        _logger.LogInformation("Grain {GrainId} deactivate requested", this.GetGrainId());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Protobuf RPC method invocation - delegates to shared RpcInvoker
    /// </summary>
    public Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        if (_agent == null)
            throw new InvalidOperationException("Agent not initialized");

        return RpcInvoker.InvokeAsync(_agent, requestBytes, _logger, CancellationToken.None);
    }

    #endregion
}

/// <summary>
/// IEventPublisher implementation for Grain
/// 
/// Supports two sending methods (all through Stream, non-blocking):
/// 1. Broadcast propagation: PublishEventAsync(event, Direction) - based on hierarchy relationships
/// 2. Point-to-point: SendToAsync(targetId, event) - directly to specified Agent
/// </summary>
internal class GrainEventPublisher : IEventPublisher
{
    private readonly OrleansGAgentGrain _grain;
    private readonly IMessageStream? _stream;
    private readonly OrleansStreamFactory? _streamFactory;
    private readonly ILogger _logger;

    public GrainEventPublisher(
        OrleansGAgentGrain grain, 
        IMessageStream? stream,
        OrleansStreamFactory? streamFactory,
        ILogger logger)
    {
        _grain = grain;
        _stream = stream;
        _streamFactory = streamFactory;
        _logger = logger;
    }

    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt, 
        EventDirection direction = EventDirection.Down, 
        CancellationToken ct = default,
        bool isInternalCall = false) 
        where TEvent : IMessage
    {
        var grainId = _grain.GetPrimaryKeyString();
        
        // Create EventEnvelope
        // GrainEventPublisher is always used for internal Agent calls, so always set PublisherId
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = grainId,  // Always set for internal Grain publishing
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = direction,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString()
        };

        _logger.LogDebug("Grain {GrainId} publishing event {EventId} with direction {Direction}",
            grainId, envelope.Id, direction);

        if (_stream != null)
        {
            // Publish through unified IMessageStream (handles serialization internally)
            await _stream.ProduceAsync(envelope, ct);
        }
        else
        {
            _logger.LogWarning("Stream not available for Grain {GrainId}, event {EventId} not published",
                grainId, envelope.Id);
        }

        return envelope.Id;
    }

    /// <summary>
    /// Point-to-point send - directly send to specified Agent's Stream (non-blocking)
    /// </summary>
    public async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var grainId = _grain.GetPrimaryKeyString();

        // Create EventEnvelope for P2P
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = grainId,
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId,
            OnArrivalDirection = onArrivalDirection
        };

        _logger.LogDebug(
            "Grain {GrainId} sending P2P event {EventId} to {TargetAgentId} via Stream (non-blocking)",
            grainId, envelope.Id, targetAgentId);

        // Get target Agent's stream using Factory (supports both Orleans/MassTransit)
        if (_streamFactory != null)
        {
            try
            {
                var targetStream = await _streamFactory.CreateStreamAsync(targetAgentId, null, _grain.GetStreamProvider);
                await targetStream.ProduceAsync(envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send P2P event to Agent {TargetId}", targetAgentId);
            }
        }
        else
        {
            _logger.LogWarning("StreamFactory not available, cannot send P2P event to Agent {TargetId}", targetAgentId);
        }

        return envelope.Id;
    }
}
