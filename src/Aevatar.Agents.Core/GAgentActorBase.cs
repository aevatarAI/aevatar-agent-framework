using System.Diagnostics;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Internal;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.Rpc;
using Aevatar.Agents.Core.Runtime;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core;

/// <summary>
/// Base class for Agent Actor
/// Provides standard implementation of event propagation logic
/// </summary>
public abstract class GAgentActorBase : IGAgentActor, IActorHierarchyOperations
{
    // ============ Fields ============

    protected readonly IGAgent Agent;
    private ILogger _logger = NullLogger.Instance;
    protected EventRouter EventRouter;

    // ============================================================
    //  Interruptible Runs (Latest-wins, opt-in by metadata)
    //
    //  中文说明：
    //  - 默认不改变任何行为；仅当 envelope.context_metadata / rpc metadata 带 run_id 时启用。
    //  - 运行时需保证同一 actor 的消息串行处理（Local/Orleans/ProtoActor），
    //    这里仅做 run 绑定 + control message（RunControlEvent）处理。
    // ============================================================
    private readonly IRunManager _runManager = new RunManager();

    // Improved event deduplication mechanism
    protected IEventDeduplicator EventDeduplicator { get; set; }

    // EventRouter factory for creating EventRouter with DI support
    // Internal to allow EventRouterFactoryInjector to replace it
    internal EventRouterFactory _eventRouterFactory = new();

    /// <summary>
    /// Context propagator for injecting context into events.
    /// Internal to allow injection via AgentContextAccessorInjector.
    /// </summary>
    internal AgentContextPropagator? ContextPropagator;

    /// <summary>
    /// Logger property - supports automatic injection
    /// </summary>
    protected ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    // ============ Constructor ============

    /// <summary>
    /// Full constructor - Agent and Logger
    /// </summary>
    protected GAgentActorBase(IGAgent agent)
    {
        Agent = agent ?? throw new ArgumentNullException(nameof(agent));

        // Note: EventRouterFactory should be injected by EventRouterFactoryInjector
        // before EventRouter is created. The factory injection happens in Actor factories.
        // EventRouter creation is deferred to after factory injection via a lazy pattern
        // or we create it with default factory here and recreate if needed.

        // For now, create EventRouter with default factory (no DI)
        // It will be recreated if factory is injected later
        EventRouter = _eventRouterFactory.CreateEventRouter(
            agent.Id,
            SendEventToActorAsync,
            SendToSelfAsync,
            _logger
        );

        // Create event deduplicator (using improved MemoryCache implementation)
        EventDeduplicator = new MemoryCacheEventDeduplicator(
            new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 50_000,
                EnableAutoCleanup = true
            }
        );

        // Use AgentEventPublisherInjector to inject EventPublisher
        AgentEventPublisherInjector.InjectEventPublisher(agent, this);
    }

    /// <summary>
    /// Initialize EventRouter with current factory
    /// Called after EventRouterFactory is injected to recreate EventRouter with DI support
    /// </summary>
    private void InitializeEventRouter()
    {
        EventRouter = _eventRouterFactory.CreateEventRouter(
            Agent.Id,
            SendEventToActorAsync,
            SendToSelfAsync,
            _logger
        );
    }

    // ============ IGAgentActor Implementation ============

    public string Id => Agent.Id;

    public IGAgent GetAgent() => Agent;

    public Task<string> GetDescriptionAsync() => Agent.GetDescriptionAsync();

    // ============ Hierarchy Management ============

    protected internal virtual async Task AddChildAsync(string childId, CancellationToken ct = default)
    {
        await EventRouter.AddChildAsync(childId, ct);
    }

    public async Task RegisterAsync(string childId, CancellationToken ct = default)
    {
        await EventRouter.AddChildAsync(childId, ct);
        
    }

    protected internal virtual async Task RemoveChildAsync(string childId, CancellationToken ct = default)
    {
        await EventRouter.RemoveChildAsync(childId, ct);
    }

    protected internal virtual async Task SetParentAsync(string parentId, CancellationToken ct = default)
    {
        await EventRouter.SetParentAsync(parentId, ct);
    }

    protected internal virtual async Task ClearParentAsync(CancellationToken ct = default)
    {
        await EventRouter.ClearParentAsync(ct);
    }

    public virtual Task<IReadOnlyList<string>> GetChildrenAsync()
    {
        return Task.FromResult(EventRouter.GetChildren());
    }

    public virtual Task<string?> GetParentAsync()
    {
        return Task.FromResult(EventRouter.GetParent());
    }

    #region IActorHierarchyOperations Explicit Implementation

    async Task IActorHierarchyOperations.AddChildAsync(string childId, CancellationToken ct)
        => await AddChildAsync(childId, ct);

    async Task IActorHierarchyOperations.RemoveChildAsync(string childId, CancellationToken ct)
        => await RemoveChildAsync(childId, ct);

    async Task IActorHierarchyOperations.SetParentAsync(string parentId, CancellationToken ct)
        => await SetParentAsync(parentId, ct);

    async Task IActorHierarchyOperations.ClearParentAsync(CancellationToken ct)
        => await ClearParentAsync(ct);

    #endregion

    // ============ Event Publishing (IEventPublisher Implementation) ============

    async Task<string> IEventPublisher.PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction,
        CancellationToken ct,
        bool isInternalCall)
    {
        return await PublishEventAsync(evt, direction, ct, isInternalCall);
    }

    async Task<string> IEventPublisher.SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection,
        CancellationToken ct,
        bool isInternalCall)
    {
        return await SendToAsync(targetAgentId, evt, onArrivalDirection, ct, isInternalCall);
    }

    // ============ Event Publishing and Routing ============

    /// <param name="isInternalCall">If true (Agent internal), keeps PublisherId; if false (external), clears it</param>
    public virtual async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var stopwatch = Stopwatch.StartNew();

        // Use EventRouter to create EventEnvelope
        var envelope = EventRouter.CreateEventEnvelope(evt, direction);
        
        // External calls: clear PublisherId so Agent can handle the event
        // Internal calls: keep PublisherId for self-handling check
        if (!isInternalCall)
        {
            envelope.PublisherId = "";
        }

        // Inject context metadata into envelope
        ContextPropagator?.InjectContext(envelope);

        using var scope = LoggingScope.CreateAgentScope(
            Logger,
            Id,
            "PublishEvent",
            new Dictionary<string, object>
            {
                ["EventId"] = envelope.Id,
                ["EventType"] = typeof(TEvent).Name,
                ["Direction"] = direction.ToString()
            });

        Logger.LogDebug("Agent {AgentId} publishing event {EventId} with direction {Direction}",
            Id, envelope.Id, direction);

        try
        {
            // Route event through EventRouter
            await EventRouter.RouteEventAsync(envelope, ct);

            // Record publish metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("direction", direction.ToString()));

            return envelope.Id;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "ActorPublishEvent");
            throw;
        }
    }

    /// <summary>
    /// Point-to-point send - Direct delivery to specified agent
    /// </summary>
    /// <param name="isInternalCall">If true (Agent internal), keeps PublisherId; if false (external), clears it</param>
    public virtual async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var stopwatch = Stopwatch.StartNew();

        // Create EventEnvelope for point-to-point message
        var envelope = EventRouter.CreateEventEnvelope(evt, onArrivalDirection);
        envelope.TargetAgentId = targetAgentId.ToString();
        envelope.OnArrivalDirection = onArrivalDirection;
        
        // External calls: clear PublisherId
        if (!isInternalCall)
        {
            envelope.PublisherId = "";
        }

        // Inject context metadata into envelope
        ContextPropagator?.InjectContext(envelope);

        using var scope = LoggingScope.CreateAgentScope(
            Logger,
            Id,
            "SendTo",
            new Dictionary<string, object>
            {
                ["EventId"] = envelope.Id,
                ["EventType"] = typeof(TEvent).Name,
                ["TargetAgentId"] = targetAgentId.ToString(),
                ["OnArrivalDirection"] = onArrivalDirection.ToString()
            });

        Logger.LogDebug(
            "Agent {AgentId} sending P2P event {EventId} to {TargetAgentId}, onArrival={OnArrivalDirection}",
            Id, envelope.Id, targetAgentId, onArrivalDirection);

        try
        {
            // Direct send to target actor (no broadcast)
            await SendEventToActorAsync(targetAgentId, envelope, ct);

            // Record metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("mode", "point-to-point"),
                new KeyValuePair<string, object?>("target", targetAgentId.ToString()));

            return envelope.Id;
        }
        catch (Exception ex)
        {
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "ActorSendTo");
            throw;
        }
    }

    /// <summary>
    /// Handle received event (standard flow)
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";
        var isPointToPoint = !string.IsNullOrEmpty(envelope.TargetAgentId);

        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        Logger.LogDebug("Agent {AgentId} handling event {EventId} (P2P={IsP2P})", 
            Id, envelope.Id, isPointToPoint);

        // Use improved event deduplication mechanism
        if (!await EventDeduplicator.TryRecordEventAsync(envelope.Id))
        {
            Logger.LogDebug("Skipping duplicate event {EventId}", envelope.Id);
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("reason", "Duplicate"));
            return;
        }

        // Use EventRouter to check if event should be processed
        if (!EventRouter.ShouldProcessEvent(envelope))
        {
            // Record skipped event
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("reason", "FilteredOut"));
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Process event
            await ProcessEventAsync(envelope, ct);

            // Record processing metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);

            // Handle propagation based on message type
            if (isPointToPoint)
            {
                // Point-to-point message: check OnArrivalDirection
                await HandlePointToPointPropagationAsync(envelope, ct);
            }
            else
            {
                // Broadcast message: standard propagation
                await HandleBroadcastPropagationAsync(envelope, ct);
            }
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "ActorHandleEvent");
            throw;
        }
    }

    /// <summary>
    /// Handle point-to-point message propagation based on OnArrivalDirection
    /// </summary>
    private async Task HandlePointToPointPropagationAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Check OnArrivalDirection to determine post-arrival behavior
        if (envelope.OnArrivalDirection == EventDirection.Unspecified)
        {
            // Pure P2P: no propagation, message stops here
            Logger.LogDebug("P2P event {EventId} arrived, no further propagation", envelope.Id);
            return;
        }

        // Create new envelope for propagation (convert from P2P to broadcast)
        var propagationEnvelope = envelope.Clone();
        propagationEnvelope.TargetAgentId = string.Empty; // Clear P2P target
        propagationEnvelope.Direction = envelope.OnArrivalDirection;
        propagationEnvelope.OnArrivalDirection = EventDirection.Unspecified;
        propagationEnvelope.PublisherId = Id.ToString(); // This agent becomes the new publisher

        Logger.LogDebug(
            "P2P event {EventId} arrived, continuing as broadcast with direction {Direction}",
            envelope.Id, envelope.OnArrivalDirection);

        // Route as broadcast from this point
        await EventRouter.RouteEventAsync(propagationEnvelope, ct);
    }

    /// <summary>
    /// Handle broadcast message propagation (standard hierarchical flow)
    /// </summary>
    private async Task HandleBroadcastPropagationAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Determine if propagation should continue
        // If this is a self-published event (PublisherId == Id), it has already been propagated in RouteEventAsync
        // If this is a received event (PublisherId != Id), propagation should continue
        bool isInitialPublisher = envelope.PublisherId == Id.ToString();

        if (!isInitialPublisher)
        {
            // Received event, continue propagation (recursive propagation)
            Logger.LogDebug("Agent {AgentId} continuing propagation of event {EventId} from {PublisherId}",
                Id, envelope.Id, envelope.PublisherId);
            await EventRouter.ContinuePropagationAsync(envelope, ct);
        }
    }

    /// <summary>
    /// Process event (call Agent's handler)
    /// </summary>
    protected virtual async Task ProcessEventAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // 0) Control plane: RunControlEvent (cancel/supersede)
        if (TryUnpackRunControl(envelope, out var control))
        {
            HandleRunControl(control);
            return;
        }

        // 1) Data plane: bind run context from context_metadata if present
        if (TryGetRunBinding(envelope, out var scopeId, out var runId))
        {
            if (_runManager.TryGetActive(scopeId, out var active) && active != null)
            {
                // Latest-wins guarantee: drop outputs from inactive runs.
                if (!string.Equals(active.RunId, runId, StringComparison.Ordinal))
                {
                    Logger.LogDebug(
                        "Skipping event {EventId} for inactive run {RunId} (active: {ActiveRunId}) in scope {ScopeId}",
                        envelope.Id, runId, active.RunId, scopeId);
                    return;
                }

                if (active.IsCancellationRequested)
                {
                    Logger.LogDebug(
                        "Skipping event {EventId} because active run {RunId} is canceled in scope {ScopeId}",
                        envelope.Id, runId, scopeId);
                    return;
                }

                using var runScope = RunContextScope.Begin(active);
                using var linked = LinkedToken.Begin(ct, active.Token);
                ct = linked.Token;
            }
            else
            {
                // Adopt run if none exists (best-effort). This keeps default behavior unchanged
                // unless run_id metadata is explicitly present.
                var adopted = _runManager.StartOrReplace(scopeId, runId, reason: "adopt_run_from_event");
                using var runScope = RunContextScope.Begin(adopted);
                using var linked = LinkedToken.Begin(ct, adopted.Token);
                ct = linked.Token;
            }
        }

        try
        {
            // Directly call Agent's HandleEventAsync method (no reflection needed)
            await Agent.HandleEventAsync(envelope, ct);
            Logger.LogDebug("ProcessEventAsync: HandleEventAsync completed for event {EventId}", envelope.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling event {EventId} in agent {AgentId}",
                envelope.Id, Id);
            throw;
        }
    }

    // ============ Abstract Methods (Implemented by Subclass) ============

    /// <summary>
    /// Send event to self (subclass implements specific transport mechanism)
    /// </summary>
    protected abstract Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Send event to specified Actor (subclass implements specific transport mechanism)
    /// </summary>
    protected abstract Task SendEventToActorAsync(string actorId, EventEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Activate Actor
    /// </summary>
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        // Reinitialize EventRouter with the injected factory (if any)
        // This ensures EventRouter uses the DI-injected store
        InitializeEventRouter();

        Logger.LogInformation("Activating agent actor {Id}", Id);

        await Agent.ActivateAsync(ct);

        // Load persisted hierarchy (if store is configured)
        await EventRouter.LoadHierarchyAsync(ct);

        // Call derived class activation logic
        await OnActivateAsync(ct);
    }

    /// <summary>
    /// Derived classes should override this method to perform specific activation logic
    /// </summary>
    protected virtual Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Deactivate Actor
    /// </summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await Agent.DeactivateAsync(ct);

        await OnDeactivateAsync(ct);
    }

    /// <summary>
    /// Derived classes should override this method to perform specific deactivation logic
    /// </summary>
    protected virtual Task OnDeactivateAsync(CancellationToken ct) => Task.CompletedTask;

    // ============ RPC Method Invocation ============

    /// <summary>
    /// Invoke RPC method on Agent via Protobuf.
    /// Default implementation uses shared RpcInvoker.
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    public virtual Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        // Bind run context if run_id is provided via RpcRequest.metadata.
        // This makes RPC entrypoints cancelable via RunContextScope + token injection in RpcInvoker.
        var request = RpcRequest.Parser.ParseFrom(requestBytes);
        if (TryGetRpcRunBinding(request, out var scopeId, out var runId))
        {
            if (string.IsNullOrWhiteSpace(scopeId))
            {
                scopeId = Id.ToString();
            }

            var ctx = _runManager.StartOrReplace(scopeId, runId, reason: "rpc_start");
            using var runScope = RunContextScope.Begin(ctx);
            return RpcInvoker.InvokeAsync(Agent, requestBytes, Logger, ctx.Token);
        }

        return RpcInvoker.InvokeAsync(Agent, requestBytes, Logger, CancellationToken.None);
    }

    private bool TryGetRunBinding(EventEnvelope envelope, out string scopeId, out string runId)
    {
        scopeId = string.Empty;
        runId = string.Empty;

        if (!TryGetContextMetadataString(envelope, RunContextScope.MetadataKeys.RunId, out runId))
            return false;

        // Prefer explicit scope id; otherwise keep scope local to this actor.
        TryGetContextMetadataString(envelope, RunContextScope.MetadataKeys.RunScopeId, out scopeId);
        scopeId = string.IsNullOrWhiteSpace(scopeId) ? Id.ToString() : scopeId.Trim();
        runId = runId.Trim();

        return scopeId.Length > 0 && runId.Length > 0;
    }

    private static bool TryGetContextMetadataString(EventEnvelope envelope, string key, out string value)
    {
        value = string.Empty;
        if (envelope.ContextMetadata == null) return false;
        if (!envelope.ContextMetadata.TryGetValue(key, out var ctxVal)) return false;
        if (ctxVal.ValueCase != ContextValue.ValueOneofCase.StringValue) return false;
        value = ctxVal.StringValue ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryUnpackRunControl(EventEnvelope envelope, out RunControlEvent control)
    {
        control = new RunControlEvent();
        if (envelope.Payload == null) return false;
        if (!envelope.Payload.Is(RunControlEvent.Descriptor)) return false;
        control = envelope.Payload.Unpack<RunControlEvent>();
        return true;
    }

    private void HandleRunControl(RunControlEvent control)
    {
        var scopeId = string.IsNullOrWhiteSpace(control.ScopeId) ? Id.ToString() : control.ScopeId.Trim();
        var targetRunId = (control.TargetRunId ?? string.Empty).Trim();
        if (targetRunId.Length == 0) return;

        if (!_runManager.TryGetActive(scopeId, out var active) || active == null)
            return;

        // Idempotent: do not cancel a newer run.
        if (!string.Equals(active.RunId, targetRunId, StringComparison.Ordinal))
            return;

        active.MarkSuperseded(control.SupersededByRunId, control.Reason);
        active.Cancel();

        Logger.LogInformation(
            "Run control: {Action} scope={ScopeId} target={TargetRunId} superseded_by={SupersededBy}",
            control.Action, scopeId, targetRunId, control.SupersededByRunId);
    }

    private static bool TryGetRpcRunBinding(RpcRequest request, out string scopeId, out string runId)
    {
        scopeId = string.Empty;
        runId = string.Empty;

        if (request.Metadata == null) return false;
        if (!request.Metadata.TryGetValue(RunContextScope.MetadataKeys.RunId, out runId)) return false;
        request.Metadata.TryGetValue(RunContextScope.MetadataKeys.RunScopeId, out scopeId);

        scopeId = (scopeId ?? string.Empty).Trim();
        runId = (runId ?? string.Empty).Trim();
        return runId.Length > 0;
    }

    private sealed class LinkedToken : IDisposable
    {
        private readonly CancellationTokenSource? _cts;
        public CancellationToken Token { get; }

        private LinkedToken(CancellationTokenSource? cts, CancellationToken token)
        {
            _cts = cts;
            Token = token;
        }

        public static LinkedToken Begin(CancellationToken outer, CancellationToken inner)
        {
            if (!outer.CanBeCanceled) return new LinkedToken(null, inner);
            if (!inner.CanBeCanceled) return new LinkedToken(null, outer);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer, inner);
            return new LinkedToken(cts, cts.Token);
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}