using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Agent Actor implementation for Local runtime.
/// Uses LocalMessageStream or external provider as the messaging transport mechanism.
/// </summary>
public class LocalGAgentActor : GAgentActorBase
{
    private static int _activeActorCount = 0;
    private readonly LocalMessageStreamRegistry _streamRegistry;

    // Abstracted Stream
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly MessageStreamProviderOptions _providerOptions;

    // My Stream
    private IMessageStream? _myStream;
    private IMessageStreamSubscription? _selfStreamSubscription;
    private IMessageStreamSubscription? _parentStreamSubscription;

    // ============================================================
    //  Actor mailbox gate (Local runtime)
    //
    //  中文说明：
    //  - Local runtime 可能同时从多个 stream（self + parent + external provider）回调进入同一个 Agent。
    //  - 这会破坏 Actor 的单线程语义，导致 State / pending events 出现并发修改。
    //  - 这里用一个 gate 强制同一个 actor 串行处理事件（Mailbox semantics）。
    // ============================================================
    private readonly SemaphoreSlim _eventGate = new(1, 1);

    // Cache for other actors' streams (when using external provider)
    private readonly ConcurrentDictionary<string, IMessageStream> _externalActorStreams = new();

    public LocalGAgentActor(
        IGAgent agent,
        LocalMessageStreamRegistry streamRegistry,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
        : base(agent)
    {
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
        _externalStreamProvider = externalStreamProvider;
        _providerOptions = providerOptions?.Value ?? new MessageStreamProviderOptions();

        InitializeStream();
    }

    private void InitializeStream()
    {
        // Determine Provider
        var providerType = _providerOptions.Provider;
        if (_providerOptions.Runtime.TryGetValue("Local", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            // Use External Provider (MassTransit)
            // Pass Agent Category as Category for dynamic routing
            var agentCategory = Agent.GetAgentCategory();
            _myStream = _externalStreamProvider.GetStream(Id, agentCategory);
        }
        else
        {
            // Use Local Stream (Default)
            _myStream = _streamRegistry.GetOrCreateStream(Id);
        }
    }

    private IMessageStream GetActorStream(string actorId)
    {
        // Determine Provider
        var providerType = _providerOptions.Provider;
        if (_providerOptions.Runtime.TryGetValue("Local", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            // For external actors, we pass null as category (default topic)
            return _externalActorStreams.GetOrAdd(actorId, id => _externalStreamProvider.GetStream(id, null));
        }
        else
        {
            // Local Stream logic
            var stream = _streamRegistry.GetStream(actorId);
            if (stream == null)
            {
                // In local mode, if target stream doesn't exist, we can't create it blindly
                // But we can return a placeholder or throw immediately
                // For compatibility, we'll try to get it, assuming caller handles null check
                // However, the signature returns IMessageStream, so we rely on _streamRegistry handling
                return _streamRegistry.GetOrCreateStream(actorId); // Auto-create stream if missing in Local?
            }

            return stream;
        }
    }

    // ============ Hierarchy Management ============
    // (Retaining existing logic but adapting to use _myStream abstraction where possible)
    // Note: Local hierarchy logic heavily relies on _streamRegistry for parent/child discovery.
    // If using MassTransit, we need to decide if we use MT for hierarchy or Local registry.
    // For now, let's assume Hierarchy control remains Local-registry based for simplicity unless migrated fully.

    // ... (Hierarchy implementation omitted for brevity, will rely on base or specific local logic)
    // Ideally, SetParentAsync should use GetActorStream(parentId) to subscribe.

    protected override async Task SetParentAsync(string parentId, CancellationToken ct = default)
    {
        if (EventRouter.GetParent() != null)
        {
            await ClearParentAsync(ct);
        }

        await base.SetParentAsync(parentId, ct);

        // Subscribe to parent's stream
        var parentStream = GetActorStream(parentId);
        if (parentStream != null)
        {
            bool CombinedFilter(EventEnvelope envelope)
            {
                // ... (Same filter logic)
                if (envelope.Direction == EventDirection.Down) return false;
                if (envelope.Direction == EventDirection.Up && envelope.Publishers.Contains(Id.ToString()))
                    return false;
                return true;
            }

            _parentStreamSubscription = await parentStream.SubscribeAsync<EventEnvelope>(
                async envelope =>
                {
                    try
                    {
                        await _eventGate.WaitAsync(ct);
                        try
                        {
                            // Route through ActorBase handler to keep propagation semantics consistent.
                            await HandleEventAsync(envelope, ct);
                        }
                        finally
                        {
                            _eventGate.Release();
                        }

                        if (envelope.Direction == EventDirection.Down)
                        {
                            await EventRouter.ContinuePropagationAsync(envelope, ct);
                        }
                        else if (envelope.Direction == EventDirection.Both)
                        {
                            var downOnlyEnvelope = envelope.Clone();
                            downOnlyEnvelope.Direction = EventDirection.Down;
                            await EventRouter.ContinuePropagationAsync(downOnlyEnvelope, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error handling event {EventId} from parent stream on agent {AgentId}",
                            envelope.Id, Id);
                    }
                },
                CombinedFilter,
                ct);
        }
    }

    protected override async Task ClearParentAsync(CancellationToken ct = default)
    {
        await base.ClearParentAsync(ct);
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
        }
    }

    // ============ Abstract Method Implementation ============

    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_myStream != null)
        {
            await _myStream.ProduceAsync(envelope, ct);
        }
    }

    protected override async Task SendEventToActorAsync(string actorId, EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var targetStream = GetActorStream(actorId);
            await targetStream.ProduceAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send event to actor {ActorId}", actorId);
        }
    }

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        if (_myStream != null)
        {
            // Store subscription handle for proper cleanup in OnDeactivateAsync
            _selfStreamSubscription = await _myStream.SubscribeAsync<EventEnvelope>(
                async envelope =>
                {
                    Logger.LogDebug("[SUBSCRIPTION] Agent {AgentId} received event {EventId} from stream", Id,
                        envelope.Id);
                    await _eventGate.WaitAsync(ct);
                    try
                    {
                        await HandleEventAsync(envelope, ct);
                    }
                    finally
                    {
                        _eventGate.Release();
                    }
                },
                null,
                ct);
        }

        Logger.LogInformation("LocalGAgentActor {Id} activated", Id);

        var count = Interlocked.Increment(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating agent {AgentId}", Id);

        // Unsubscribe from self stream
        if (_selfStreamSubscription != null)
        {
            await _selfStreamSubscription.UnsubscribeAsync();
            _selfStreamSubscription = null;
        }

        // Unsubscribe from parent stream (if still subscribed)
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
        }

        // Clean up local stream registry
        if (_myStream is LocalMessageStream)
        {
            _streamRegistry.RemoveStream(Id);
        }

        var count = Interlocked.Decrement(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
    }

    // RPC: Inherited from GAgentActorBase (uses shared RpcInvoker)
}