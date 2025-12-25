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
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 父节点 ID
    /// </summary>
    [Id(2)]
    public string? ParentId { get; set; }

    /// <summary>
    /// 子节点 ID 列表
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
/// Orleans GAgent Grain - Agent 业务逻辑在 Silo 内执行
/// 
/// 职责:
/// 1. 在 Silo 内创建和持有 Agent 实例
/// 2. 在 Silo 内执行 Agent 业务逻辑 (HandleEventAsync)
/// 3. 存储层级关系 (Parent/Children)
/// 4. 管理 Orleans Streams 订阅
/// </summary>
[Reentrant]
public class OrleansGAgentGrain : Grain, IGAgentGrain
{
    // Grain 持久化状态
    private readonly IPersistentState<OrleansAgentState> _grainState;

    // Agent 实例 - 在 Silo 内创建和执行
    private IGAgent? _agent;
    private bool _isInitialized;

    // Unified Message Stream (可以是 Orleans Stream 或 MassTransit Stream)
    private IMessageStream? _myStream;
    private IMessageStreamSubscription? _streamSubscription;

    // Stream Factory - 统一管理 Stream 创建逻辑
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

        // Initialize Stream Factory - 统一管理 Orleans/MassTransit Stream 选择逻辑
        // Factory 会从 DI 获取 IMessageStreamProvider (MassTransit) 和配置
        _streamFactory = ServiceProvider.GetService<OrleansStreamFactory>()
            ?? ActivatorUtilities.CreateInstance<OrleansStreamFactory>(ServiceProvider);

        // Initialize Stream (根据配置选择 Orleans Stream 或 MassTransit)
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
    /// Initialize Stream - 根据配置选择 Orleans Stream 或 MassTransit Stream
    /// 与 LocalGAgentActor 保持一致的配置驱动模式
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

            // 使用 Factory 统一创建 Stream (自动选择 Orleans/MassTransit)
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
    /// 优势（相比 RPC）：
    /// 1. 非阻塞：发送后立即返回，不等待目标处理
    /// 2. 顺序保证：Kafka 分区内消息顺序有保证
    /// 3. 解耦：发送者和接收者完全解耦
    /// 4. 弹性：接收者不可用时消息在队列中缓存
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
    /// 使用 Factory 统一创建（自动选择 MassTransit 或 Orleans Stream）
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
            
            // 使用 Factory 创建目标 Agent 的 Stream (同步获取，因为 Factory.CreateStreamAsync 本质上是同步的)
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
    /// 所有传播都通过 Stream 完成，不使用 RPC，避免阻塞
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
                // 并行发送，不等待
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
        //  AgentId 统一规范（对齐 docs/AGENT_ID_GUIDE.md）
        //
        //  Orleans GrainKey / StreamKey 统一使用完整 ActorId：
        //    "AgentTypeShortName:RawId"
        //
        //  WHY:
        //  - 仅使用 RawId 会导致跨类型冲突（同 RawId 不同 AgentType 会复用同一个 Grain）
        //  - PublisherId / self-handling / StreamKey 必须一致，否则会出现“自发事件无法识别为 self”的隐性 bug
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
        // 支持广播传播和点对点发送 (全部通过 Stream，非阻塞)
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
    /// 事件流处理流程:
    /// 1. Agent 处理事件 (唯一调用点)
    /// 2. 根据 Direction 传播到 Parent/Children (通过 Stream，非阻塞)
    ///    - 有子节点才发 Down
    ///    - 有父节点才发 Up
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

            // ✅ Step 1: Execute business logic in Silo (唯一调用点)
            await _agent.HandleEventAsync(envelope, CancellationToken.None);
            
            // ✅ Step 2: Direction-based propagation via Stream (非阻塞)
            // 只有当有订阅者（Parent/Children）时才传播
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
    /// 用于接收来自 Stream 的事件：
    /// 1. 外部系统的事件输入 (如 MassTransit Kafka)
    /// 2. 其他 Agent 通过 Stream 发送的事件
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
            // 跳过自己发布的事件 (避免重复处理)
            var grainKey = this.GetPrimaryKeyString();
            if (envelope.PublisherId == grainKey)
            {
                _logger.LogDebug("Skipping self-published event {EventId} from stream in Grain {GrainId}", 
                    envelope.Id, this.GetGrainId());
                return;
            }
            
            _logger.LogDebug("Grain {GrainId} processing external event {EventId} from stream", 
                this.GetGrainId(), envelope.Id);

            // 外部事件：执行业务逻辑并传播
            await _agent.HandleEventAsync(envelope, CancellationToken.None);
            
            // 如果有 Direction，继续传播
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
            // 与 Actor.Id / StreamId 对齐：返回完整 ActorId（"Type:RawId"）
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

        return RpcInvoker.InvokeAsync(_agent, requestBytes, _logger);
    }

    #endregion
}

/// <summary>
/// IEventPublisher implementation for Grain
/// 
/// 支持两种发送方式 (全部通过 Stream，非阻塞):
/// 1. 广播传播: PublishEventAsync(event, Direction) - 基于层级关系
/// 2. 点对点:   SendToAsync(targetId, event) - 直接发给指定 Agent
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
    /// 点对点发送 - 直接发送到指定 Agent 的 Stream (非阻塞)
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
