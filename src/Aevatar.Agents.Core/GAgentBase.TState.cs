using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Stateful agent base class
/// Extends the non-generic GAgentBase with state management capabilities
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
public abstract class GAgentBase<TState> : GAgentBase, IStateGAgent<TState>
    where TState : class, IMessage<TState>, new()
{
    // ============ Fields ============

    private TState _state = new();

    /// <summary>
    /// State object - should only be modified within event handlers.
    /// Direct State assignment is protected, but individual property modifications cannot be intercepted
    /// for Protobuf-generated classes. Follow best practices:
    /// - Only modify State within [EventHandler] methods
    /// - Use events to trigger state changes
    /// - Direct modifications outside event handlers break the Actor model consistency
    /// </summary>
    protected TState State
    {
        get
        {
            // For development/debug builds, we can add a warning when accessing State outside handlers
#if DEBUG
            if (!StateProtectionContext.IsModifiable)
            {
                var callerMethod = new StackFrame(1)?.GetMethod()?.Name ?? "Unknown";
                if (!IsAllowedStateAccessMethod(callerMethod))
                {
                    Debug.WriteLine(
                        $"WARNING: State accessed from '{callerMethod}' outside event handler context. " +
                        "State should only be modified within event handlers.");
                }
            }
#endif
            return _state;
        }
        set
        {
            StateProtectionContext.EnsureModifiable("Direct State assignment");
            
            // If we have events in the stream (Version > 0), we are in Event Sourcing mode.
            // Direct state modification is not allowed in this mode to ensure consistency.
            // State should only be updated via RaiseEvent -> ApplyEvent.
            if (_currentVersion > 0)
            {
                throw new InvalidOperationException(
                    "Direct State modification is not allowed when Event Sourcing is active (Version > 0). " +
                    "Use RaiseEvent to modify state, or clear EventStore to reset version.");
            }

            _state = value;
        }
    }

    /// <summary>
    /// Validates if the current context allows State modification.
    /// Throws an exception if not in a valid context.
    /// </summary>
    protected void ValidateStateModificationContext(string operationName = "State modification")
    {
        StateProtectionContext.EnsureModifiable(operationName);
    }

#if DEBUG
    protected virtual bool IsAllowedStateAccessMethod(string methodName)
    {
        // Allow certain methods to access State without warning
        return methodName switch
        {
            nameof(GetState) => true,
            nameof(GetDescription) => true,
            nameof(GetDescriptionAsync) => true,
            nameof(OnActivateAsync) => true,
            nameof(ToString) => true,
            nameof(HandleEventAsync) => true,
            _ => false
        };
    }
#endif

    /// <summary>
    /// StateStore (injected by Actor layer)
    /// </summary>
    protected IStateStore<TState>? StateStore { get; set; }


    // ============ Event Sourcing Dependencies ============

    private static readonly IEventTypeResolver _defaultResolver = new ProtobufEventTypeResolver();

    /// <summary>
    /// Event type resolver (can be overridden or injected)
    /// </summary>
    protected virtual IEventTypeResolver EventTypeResolver => _defaultResolver;

    /// <summary>
    /// EventStore for persistence (supports injection)
    /// </summary>
    protected IEventStore? EventStore { get; set; }

    private long _currentVersion;

    // Batch event management
    private readonly List<AgentStateEvent> _pendingEvents = [];

    // ============ Constructors ============

    public GAgentBase()
    {
    }

    public GAgentBase(string id) : base(id)
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // StateProjector is injected by StateProjectorInjector after agent creation
        
        // State initialization strategy:
        // - If EventStore is configured: replay events (may or may not activate event sourcing)
        // - If event sourcing is not active (version==0), fall back to StateStore
        // This avoids blocking StateStore just because EventStore is injected.
        
        if (EventStore != null)
        {
            // Event Sourcing mode: State is rebuilt from events (includes snapshot loading)
            // No need for StateStore - EventStore handles all persistence
            await ReplayEventsAsync(ct);
        }

        if (_currentVersion == 0 && StateStore != null)
        {
            // Simple state mode: Load state directly from StateStore
            // Only load if there's persisted state, otherwise keep current state
            // (allows subclass to set default values before calling base.OnActivateAsync)
            using (StateProtectionContext.BeginEventHandlerScope())
            {
                var loadedState = await StateStore.LoadAsync(Id, ct);
                if (loadedState != null)
                {
                    _state = loadedState;
                }
            }
        }
    }

    // ============ IStateGAgent Implementation ============

    public TState GetState()
    {
        return _state.Clone(); // Return clone of state for read-only access
    }

    // ============ Event Handling with State Persistence ============

    /// <summary>
    /// Handle event with automatic state loading and saving.
    /// Uses mutually exclusive persistence strategies:
    /// - EventStore mode: Events + Snapshots handle all persistence (no StateStore)
    /// - StateStore mode: Simple state load/save per event
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Persistence strategy:
        // - EventStore may be injected but event sourcing is active only when version > 0.
        // - StateStore should not be blocked until event sourcing actually starts.
        var eventStoreEnabled = EventStore != null;
        var eventSourcingActive = eventStoreEnabled && _currentVersion > 0;
        
        // 1. Load State (only when event sourcing is not active)
        if (!eventSourcingActive && StateStore != null)
        {
            using (StateProtectionContext.BeginEventHandlerScope())
            {
                // Only load if there's persisted state, otherwise keep current in-memory state
                var loadedState = await StateStore.LoadAsync(Id, ct);
                if (loadedState != null)
                {
                    _state = loadedState;
                }
            }
        }

        // 2. Call core event handling implementation
        await HandleEventCoreAsync(envelope, ct);

        // 3. Persist state changes
        if (eventStoreEnabled)
        {
            // Event Sourcing mode: Persist via events + snapshots (best-effort).
            // Pass notifyStateChanged=false to avoid duplicate call (we call it below).
            await ConfirmEventsAsync(ct, notifyStateChanged: false);
        }

        if (StateStore != null && _currentVersion == 0)
        {
            // StateStore mode (event sourcing not active): Direct state persistence.
            await StateStore.SaveAsync(Id, _state, ct);
        }

        // 4. State change hook - notify external systems (always, for CQRS projection)
        await OnStateChangedAsync(_state, ct);
    }

    // ============ State Change Hook ============

    /// <summary>
    /// State projector for CQRS pattern (injected from DI)
    /// </summary>
    protected IStateProjector? StateProjector { get; set; }

    /// <summary>
    /// Called after state has been changed and saved.
    /// Default implementation projects state to CQRS read model if StateProjector is configured.
    /// Override to customize state change notifications.
    /// </summary>
    /// <param name="state">The current state after changes</param>
    /// <param name="ct">Cancellation token</param>
    protected virtual async Task OnStateChangedAsync(TState state, CancellationToken ct = default)
    {
        // Default: project to CQRS read model if configured
        if (StateProjector != null)
        {
            Logger?.LogDebug("OnStateChangedAsync called for agent {AgentId} ({AgentType}), StateProjector is configured", 
                Id, GetType().Name);
            await ProjectStateAsync(state, ct);
        }
        else
        {
            // StateProjector is optional. Logging this as Warning is too noisy and will flood logs.
            Logger?.LogDebug("OnStateChangedAsync skipped projection for agent {AgentId} ({AgentType}) because StateProjector is null", 
                Id, GetType().Name);
        }
    }

    /// <summary>
    /// Project state to CQRS read model.
    /// Can be called manually or automatically via OnStateChangedAsync.
    /// This is the unified entry point for CQRS projection, independent of stream implementation.
    /// </summary>
    protected async Task ProjectStateAsync(TState state, CancellationToken ct = default)
    {
        if (StateProjector == null)
        {
            Logger?.LogDebug("StateProjector not configured for agent {AgentId} ({AgentType}), state will not be projected", 
                Id, GetType().Name);
            return;
        }

        try
        {
            // Use event sourcing version if available, otherwise use timestamp for optimistic concurrency
            var projectionVersion = _currentVersion > 0 ? _currentVersion : DateTime.UtcNow.Ticks;
            
            var wrapper = new StateWrapper
            {
                AgentId = Id.ToString(),
                AgentType = GetType().FullName ?? GetType().Name,
                StateData = Any.Pack(state),
                Version = projectionVersion,
                PublishedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await StateProjector.ProjectAsync(wrapper, ct);

            Logger?.LogDebug(
                "Projected state for agent {AgentId}, version: {Version}",
                Id, _currentVersion);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error projecting state for agent {AgentId}", Id);
        }
    }

    // ============ Event Operations ============

    /// <summary>
    /// Stage event (does not persist immediately)
    /// </summary>
    protected void RaiseEvent<TEvent>(
        TEvent evt,
        Dictionary<string, string>? metadata = null)
        where TEvent : class, IMessage
    {
        var stateEvent = new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            EventType = evt.Descriptor.FullName,
            EventData = Any.Pack(evt),
            AgentId = Id.ToString(),
            Version = _currentVersion + _pendingEvents.Count + 1,
        };

        // Add metadata
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                stateEvent.Metadata[key] = value;
            }
        }

        _pendingEvents.Add(stateEvent);
    }

    /// <summary>
    /// Commit pending events (batch persist)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="notifyStateChanged">Whether to call OnStateChangedAsync after confirming events. Default is true for RPC methods, false when called from HandleEventAsync.</param>
    protected async Task ConfirmEventsAsync(CancellationToken ct = default, bool notifyStateChanged = true)
    {
        if (_pendingEvents.Count == 0) return;

        if (EventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, events will not be persisted");
            _pendingEvents.Clear();
            return;
        }

        try
        {
            // Batch persist (pass agent type name for per-type collection routing)
            var agentTypeName = GetType().FullName;
            _currentVersion = await EventStore.AppendEventsAsync(
                Id,
                _pendingEvents,
                _currentVersion,
                agentTypeName,
                ct);

            // Apply events to state
            foreach (var evt in _pendingEvents)
            {
                await ApplyEventInternalAsync(evt, ct);
            }

            var eventCount = _pendingEvents.Count;
            _pendingEvents.Clear();

            // Check snapshot strategy
            if (SnapshotStrategy.ShouldCreateSnapshot(_currentVersion))
            {
                await CreateSnapshotInternalAsync(ct);
            }

            // Call OnStateChangedAsync if requested (default true for RPC methods, false for HandleEventAsync)
            if (notifyStateChanged)
            {
                await OnStateChangedAsync(_state, ct);
            }

            Logger?.LogDebug(
                "Confirmed {Count} events for agent {AgentId}, version: {Version}",
                eventCount, Id, _currentVersion);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error confirming events for agent {AgentId}", Id);
            throw;
        }
    }

    // ============ Pure Functional State Transition ============

    /// <summary>
    /// Pure functional state transition.
    /// Must be implemented if Event Sourcing is used.
    /// </summary>
    protected virtual void TransitionState(TState state, IMessage evt)
    {
        // Default implementation does nothing.
        // If EventStore is used, this MUST be overridden.
        if (EventStore != null)
        {
             Logger.LogWarning("TransitionState not implemented for {AgentType}, but EventStore is configured. State will not be updated from events.", GetType().Name);
        }
    }

    /// <summary>
    /// Apply event internally (optimized with type caching)
    /// </summary>
    private Task ApplyEventInternalAsync(AgentStateEvent evt, CancellationToken ct)
    {
        try
        {
            // Get or build type cache
            var typeInfo = EventTypeResolver.Resolve(evt.EventData.TypeUrl, this.GetType().Assembly);

            if (typeInfo == null)
            {
                Logger?.LogWarning(
                    "Failed to resolve type from TypeUrl {TypeUrl}",
                    evt.EventData.TypeUrl);
                return Task.CompletedTask;
            }

            // Parse message
            var message = typeInfo.Parser.ParseFrom(evt.EventData.Value);

            if (message == null)
            {
                Logger?.LogWarning("Failed to parse event {TypeName}", typeInfo.Type.Name);
                return Task.CompletedTask;
            }

            Logger?.LogDebug(
                "Applying event {TypeName} version {Version} to agent {AgentId}",
                typeInfo.Type.Name, evt.Version, Id);

            // Clone state
            var newState = State.Clone();

            // Transition state
            TransitionState(newState, message);

            // Update state
            SetState(newState);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex,
                "Error applying event {EventType} version {Version}",
                evt.EventType, evt.Version);
            throw;
        }
    }

    private void SetState(TState newState)
    {
        // Directly set the field to bypass the EventStore check in the property setter.
        // This method is only called by the framework (ApplyEventInternalAsync, ReplayEventsAsync),
        // so it is safe to update the state here.
        _state = newState;
    }

    // ============ Event Replay ============

    /// <summary>
    /// Replay events from event store
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (EventStore == null)
        {
            Logger?.LogWarning("EventStore not configured, cannot replay events");
            return;
        }

        Logger.LogInformation("Replaying events for agent {AgentId}, starting from version {CurrentVersion}", Id,
            _currentVersion);

        // Step 1: Load latest snapshot from StateStore (if available)
        // StateStore provides per-type collections (agent_snapshots_{StateType})
        if (StateStore != null)
        {
            await LoadSnapshotFromStateStoreAsync(ct);
        }

        // Step 2: Replay events after snapshot (pass agent type name for per-type collection routing)
        var fromVersion = _currentVersion + 1;
        var agentTypeName = GetType().FullName;
        var events = await EventStore.GetEventsAsync(
            Id,
            fromVersion: fromVersion,
            agentTypeName: agentTypeName,
            ct: ct);

        if (!events.Any())
        {
            return;
        }

        // Step 3: Apply events
        foreach (var evt in events.OrderBy(e => e.Version))
        {
            await ApplyEventInternalAsync(evt, ct);
            _currentVersion = evt.Version;
        }

        Logger.LogInformation(
            "Replayed {Count} events for agent {AgentId}, current version: {Version}",
            events.Count, Id, _currentVersion);
    }

    /// <summary>
    /// Load snapshot from StateStore (supports IVersionedStateStore for version tracking)
    /// </summary>
    private async Task LoadSnapshotFromStateStoreAsync(CancellationToken ct)
    {
        if (StateStore == null) return;

        try
        {
            // Try to load with version if IVersionedStateStore is available
            if (StateStore is IVersionedStateStore<TState> versionedStore)
            {
                var snapshotVersion = await versionedStore.GetCurrentVersionAsync(Id, ct);
                if (snapshotVersion > 0)
                {
                    var snapshotState = await StateStore.LoadAsync(Id, ct);
                    if (snapshotState != null)
                    {
                        SetState(snapshotState);
                        _currentVersion = snapshotVersion;
                        Logger.LogInformation(
                            "Loaded snapshot from StateStore at version {Version} for agent {AgentId}",
                            _currentVersion, Id);
                    }
                }
            }
            else
            {
                // Fallback: Load without version tracking
                var snapshotState = await StateStore.LoadAsync(Id, ct);
                if (snapshotState != null)
                {
                    SetState(snapshotState);
                    // Note: Without version tracking, we start from 0 and replay all events
                    Logger.LogInformation(
                        "Loaded snapshot from StateStore (no version) for agent {AgentId}",
                        Id);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, 
                "Failed to load snapshot from StateStore for agent {AgentId}, will replay all events",
                Id);
        }
    }

    // ============ Snapshot Operations ============

    // TODO: Read from configuration (EventSourcing:SnapshotFrequency)
    protected virtual ISnapshotStrategy SnapshotStrategy =>
        new IntervalSnapshotStrategy(100);

    /// <summary>
    /// Create snapshot using StateStore (preferred) or EventStore (fallback)
    /// 
    /// Architecture:
    /// - StateStore provides per-type collections (agent_states_{StateType})
    /// - No extra Grain RPC overhead
    /// </summary>
    private async Task CreateSnapshotInternalAsync(CancellationToken ct)
    {
        if (StateStore == null)
        {
            Logger?.LogWarning("StateStore not configured, cannot save snapshot for agent {AgentId}", Id);
            return;
        }

        // Use IVersionedStateStore if available for version tracking
        if (StateStore is IVersionedStateStore<TState> versionedStore)
        {
            await versionedStore.SaveAsync(Id, State, _currentVersion, ct);
        }
        else
        {
            await StateStore.SaveAsync(Id, State, ct);
        }

        Logger?.LogDebug(
            "Snapshot saved for agent {AgentId} at version {Version}",
            Id, _currentVersion);
    }

    public async Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        await CreateSnapshotInternalAsync(ct);
    }

    public long GetCurrentVersion() => _currentVersion;
}