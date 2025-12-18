using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain 状态存储模型
/// 包含 Agent 元数据（不含业务状态），由 Orleans GrainStorage 自动持久化
/// 
/// Note: 业务状态（Snapshot）现在由 IStateStore&lt;TState&gt; 处理，
/// 存储在按类型分表的 MongoDB 集合中（agent_states_{StateType}）
/// </summary>
[GenerateSerializer]
public class OrleansAgentState
{
    /// <summary>
    /// Agent 类型名（程序集限定名）
    /// </summary>
    [Id(0)]
    public string? AgentTypeName { get; set; }

    /// <summary>
    /// Agent 的唯一标识
    /// </summary>
    [Id(1)]
    public Guid AgentId { get; set; }

    /// <summary>
    /// 父节点 ID
    /// </summary>
    [Id(2)]
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 子节点 ID 列表
    /// </summary>
    [Id(3)]
    public List<Guid> Children { get; set; } = new();

    public OrleansAgentState() { }

    public OrleansAgentState(Guid? parentId = null)
    {
        ParentId = parentId;
        Children = new List<Guid>();
    }
}

/// <summary>
/// Orleans GAgent Grain - Agent 业务逻辑在 Silo 内执行
/// 
/// 职责:
/// 1. 在 Silo 内创建和持有 Agent 实例
/// 2. 在 Silo 内执行 Agent 业务逻辑 (HandleEventAsync)
/// 3. 存储层级关系 (Parent/Children)
/// 4. 管理 Orleans Streams 订阅
/// </summary>
public class OrleansGAgentGrain : Grain, IGAgentGrain
{
    // Grain 持久化状态
    private readonly IPersistentState<OrleansAgentState> _grainState;

    // Agent 实例 - 在 Silo 内创建和执行
    private IGAgent? _agent;
    private bool _isInitialized;

    // Orleans Stream
    private IStreamProvider? _streamProvider;
    private IAsyncStream<byte[]>? _myStream;
    private StreamSubscriptionHandle<byte[]>? _streamSubscription;

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

        // Initialize Stream
        await InitializeStreamAsync();

        // Restore Agent if previously initialized
        if (!string.IsNullOrEmpty(_grainState.State.AgentTypeName) && _grainState.State.AgentId != Guid.Empty)
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

        // Unsubscribe Stream
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from stream for Grain {GrainId}", this.GetGrainId());
            }
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private async Task InitializeStreamAsync()
    {
        var streamingOptions = ServiceProvider.GetService<IOptions<StreamingOptions>>();
        var streamNamespace = streamingOptions?.Value?.DefaultStreamNamespace ?? AevatarAgentsOrleansConstants.StreamNamespace;
        var streamProviderName = streamingOptions?.Value?.StreamProviderName ?? AevatarAgentsOrleansConstants.StreamProviderName;

        try
        {
            _streamProvider = this.GetStreamProvider(streamProviderName);
            var streamId = StreamId.Create(streamNamespace, this.GetPrimaryKeyString());
            _myStream = _streamProvider.GetStream<byte[]>(streamId);

            // Subscribe to stream for event handling
            _streamSubscription = await _myStream.SubscribeAsync(OnStreamEventReceivedAsync);

            _logger.LogDebug("Successfully subscribed to stream for Grain {GrainId}", this.GetGrainId());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize streams for Grain {GrainId}", this.GetGrainId());
        }
    }

    #endregion

    #region Agent Initialization

    public Task<bool> InitializeAgentAsync(string agentTypeName)
    {
        // Grain Key format:
        // - Preferred: "AgentId" (string guid)  ✅ consistent with P2P routing (targetAgentId only)
        // - Backward compatible: "AgentTypeShortName:AgentId"
        //
        // Always extract AgentId from the Grain key for consistency.
        var grainKey = this.GetPrimaryKeyString();
        var agentId = ExtractAgentIdFromGrainKey(grainKey);
        return InitializeAgentInternalAsync(agentTypeName, agentId, persistState: true);
    }

    /// <summary>
    /// Extract Agent ID from Grain Key
    /// Grain Key format: "AgentTypeShortName:AgentId" or just "AgentId" (for backwards compatibility)
    /// </summary>
    private static Guid ExtractAgentIdFromGrainKey(string grainKey)
    {
        var colonIndex = grainKey.LastIndexOf(':');
        if (colonIndex >= 0 && colonIndex < grainKey.Length - 1)
        {
            // New format: "AgentType:AgentId"
            return Guid.Parse(grainKey.Substring(colonIndex + 1));
        }
        // Legacy format: just the GUID
        return Guid.Parse(grainKey);
    }

    private async Task<bool> InitializeAgentInternalAsync(string agentTypeName, Guid agentId, bool persistState = false)
    {
        if (_isInitialized && _agent != null)
        {
            _logger.LogDebug("Agent already initialized in Grain {GrainId}", this.GetGrainId());
            return true;
        }

        try
        {
            _logger.LogInformation("🔧 Initializing Agent in Grain {GrainId}, Type: {AgentType}", 
                this.GetGrainId(), agentTypeName);

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

    private IGAgent? CreateAgentInstance(System.Type agentType, Guid agentId)
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

    private void SetAgentId(IGAgent agent, Guid id)
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
        AgentEventPublisherInjector.InjectEventPublisher(agent, new GrainEventPublisher(this, _myStream, _logger, GrainFactory));

        // Inject ActorFactory (for Agents that need to create child Agents)
        InjectActorFactory(agent);

        _logger.LogDebug("Injected dependencies into Agent {AgentId}", agent.Id);
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
        _logger.LogDebug("Successfully injected ActorFactory into Agent {AgentType}", agentType.Name);
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
    /// Handle event - Execute business logic in Silo
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
            _logger.LogDebug("🔥 Grain {GrainId} handling event {EventId} in Silo", 
                this.GetGrainId(), envelope.Id);

            // ✅ Execute business logic in Silo!
            await _agent.HandleEventAsync(envelope, CancellationToken.None);

            // Broadcast to stream (for children)
            if (_myStream != null)
            {
                await _myStream.OnNextAsync(envelopeBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event in Grain {GrainId}", this.GetGrainId());
            throw;
        }
    }

    /// <summary>
    /// Stream callback - Handle events from stream subscription
    /// </summary>
    private async Task OnStreamEventReceivedAsync(byte[] envelopeBytes, StreamSequenceToken? token)
    {
        // Events from stream are already handled by HandleEventAsync via RPC
        // This callback is for child agents subscribing to parent's stream
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            try
            {
                var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
                _logger.LogDebug("Grain {GrainId} stream received event {EventId}", 
                    this.GetGrainId(), envelope.Id);
            }
            catch
            {
                // Ignore parse errors in debug logging
            }
        }
    }

    #endregion

    #region Hierarchy Management

    public Task<Guid> GetIdAsync()
    {
        var grainKey = this.GetPrimaryKeyString();
        try
        {
            return Task.FromResult(ExtractAgentIdFromGrainKey(grainKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AgentId from Grain key '{GrainKey}'", grainKey);
            return Task.FromResult(Guid.Empty);
        }
    }

    public async Task AddChildAsync(Guid childId)
    {
        if (!_grainState.State.Children.Contains(childId))
        {
            _grainState.State.Children.Add(childId);
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Added child {ChildId} to Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task RemoveChildAsync(Guid childId)
    {
        if (_grainState.State.Children.Remove(childId))
        {
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Removed child {ChildId} from Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task SetParentAsync(Guid parentId)
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
        if (_grainState.State.ParentId.HasValue)
        {
            _grainState.State.ParentId = null;
            await _grainState.WriteStateAsync();
            _logger.LogInformation("Cleared parent for Grain {GrainId}", this.GetGrainId());
        }
    }

    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_grainState.State.Children.AsReadOnly());
    }

    public Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(_grainState.State.ParentId);
    }

    #endregion

    #region Legacy Methods

    [Obsolete("Use InitializeAgentAsync(agentTypeName) instead")]
    public Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null)
    {
        throw new NotSupportedException(
            "ActivateAsync is obsolete. Use InitializeAgentAsync(agentTypeName) instead.");
    }

    public Task DeactivateAsync()
    {
        _logger.LogInformation("Grain {GrainId} deactivate requested", this.GetGrainId());
        return Task.CompletedTask;
    }

    #endregion

    #region RPC Method Invocation

    /// <summary>
    /// Protobuf RPC method invocation - delegates to shared RpcInvoker
    /// </summary>
    public Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        if (_agent == null)
            throw new InvalidOperationException("Agent not initialized");

        return RpcInvoker.InvokeAsync(_agent, requestBytes, _logger);
    }

    #endregion
}

/// <summary>
/// IEventPublisher implementation for Grain
/// Publishes events through Orleans Stream
/// </summary>
internal class GrainEventPublisher : IEventPublisher
{
    private readonly OrleansGAgentGrain _grain;
    private readonly IAsyncStream<byte[]>? _stream;
    private readonly ILogger _logger;
    private readonly IGrainFactory _grainFactory;

    public GrainEventPublisher(
        OrleansGAgentGrain grain, 
        IAsyncStream<byte[]>? stream, 
        ILogger logger,
        IGrainFactory grainFactory)
    {
        _grain = grain;
        _stream = stream;
        _logger = logger;
        _grainFactory = grainFactory;
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
            // Serialize and publish to stream
            using var memStream = new MemoryStream();
            using var codedOutput = new CodedOutputStream(memStream);
            envelope.WriteTo(codedOutput);
            codedOutput.Flush();
            await _stream.OnNextAsync(memStream.ToArray());
        }
        else
        {
            _logger.LogWarning("Stream not available for Grain {GrainId}, event {EventId} not published",
                grainId, envelope.Id);
        }

        return envelope.Id;
    }

    public async Task<string> SendToAsync<TEvent>(
        Guid targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var grainId = _grain.GetPrimaryKeyString();

        // Create EventEnvelope for P2P
        // GrainEventPublisher is always used for internal Agent calls
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = grainId,  // Always set for internal Grain publishing
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId.ToString(),
            OnArrivalDirection = onArrivalDirection
        };

        _logger.LogDebug(
            "Grain {GrainId} sending P2P event {EventId} to {TargetAgentId}, onArrival={OnArrivalDirection}",
            grainId, envelope.Id, targetAgentId, onArrivalDirection);

        // Serialize envelope
        using var memStream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(memStream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();
        var envelopeBytes = memStream.ToArray();

        // Direct RPC to target Grain (no stream broadcast)
        var targetGrain = _grainFactory.GetGrain<IGAgentGrain>(targetAgentId.ToString());
        await targetGrain.HandleEventAsync(envelopeBytes);

        return envelope.Id;
    }
}
