using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor runtime Agent Actor implementation
/// Uses ProtoActorMessageStream as message transport mechanism
/// </summary>
public class ProtoActorGAgentActor : GAgentActorBase
{
    private static int _activeActorCount = 0;
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly ProtoActorMessageStream _myStream; // This Actor's Stream
    private IMessageStreamSubscription? _parentStreamSubscription; // Parent node stream subscription handle

    public ProtoActorGAgentActor(
        IGAgent agent,
        IRootContext rootContext,
        PID actorPid,
        ProtoActorMessageStreamRegistry streamRegistry)
        : base(agent)
    {
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _actorPid = actorPid ?? throw new ArgumentNullException(nameof(actorPid));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));

        // Register PID and get Stream
        _streamRegistry.RegisterPid(agent.Id, actorPid);
        _myStream = _streamRegistry.GetStream(agent.Id)!;
    }

    /// <summary>
    /// Get Proto.Actor PID
    /// </summary>
    public PID GetPid() => _actorPid;

    // ============ Hierarchy Management (Override Base Class Methods) ============

    protected override async Task SetParentAsync(string parentId, CancellationToken ct = default)
    {
        // If parent already exists, clear it first
        if (EventRouter.GetParent() != null)
        {
            await ClearParentAsync(ct);
        }

        // Call base class method to set parent
        await base.SetParentAsync(parentId, ct);

        // Subscribe to parent's stream
        var parentStream = _streamRegistry.GetStream(parentId);
        if (parentStream != null)
        {
            // Note: Event type filtering feature is deprecated
            // Because Protobuf doesn't support inheritance, effective filtering at type level is not possible
            // All event filtering should be done within Agent's event handlers based on event content

            // Create filter: filter out self-published events
            Func<EventEnvelope, bool>? combinedFilter = envelope =>
            {
                // Filter out self-published events to avoid loops
                if (envelope.PublisherId == Id.ToString())
                {
                    return false;
                }

                return true;
            };

            // Agent subscribes to parent's stream to receive group broadcast events
            _parentStreamSubscription = await parentStream.SubscribeAsync(
                async envelope =>
                {
                    // Events received from parent stream only need processing, no further propagation needed
                    // Because this event has already been broadcast in parent stream
                    // Direct call - no reflection needed since IGAgent defines HandleEventAsync
                    await Agent.HandleEventAsync(envelope, ct);
                },
                combinedFilter,
                ct);

            Logger.LogDebug("Agent {AgentId} subscribed to parent {ParentId} stream", Id, parentId);
        }
    }

    protected override async Task ClearParentAsync(CancellationToken ct = default)
    {
        // Call base class method to clear parent
        await base.ClearParentAsync(ct);

        // Unsubscribe from parent's stream
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
            Logger.LogDebug("Agent {AgentId} unsubscribed from parent stream", Id);
        }
    }

    // ============ Abstract Method Implementation ============

    /// <summary>
    /// Send event to self (via own Stream)
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        Console.WriteLine($"ProtoActor {Id} SendToSelfAsync called with event {envelope.Id}");
        Logger.LogDebug("ProtoActor {AgentId} sending event to self via stream", Id);
        await _myStream.ProduceAsync(envelope, ct);
        Logger.LogDebug("ProtoActor {AgentId} sent event to self via stream", Id);
    }

    /// <summary>
    /// Send event to specified Actor (via target Actor's Stream)
    /// </summary>
    protected override async Task SendEventToActorAsync(string actorId, EventEnvelope envelope, CancellationToken ct)
    {
        var targetStream = _streamRegistry.GetStream(actorId);
        if (targetStream != null)
        {
            await targetStream.ProduceAsync(envelope, ct);
        }
        else
        {
            Logger.LogWarning("Stream for actor {ActorId} not found", actorId);
        }
    }

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        Console.WriteLine($"ProtoActorGAgentActor.ActivateAsync called for agent {Id}");
        Logger.LogInformation("Activating agent {AgentId}", Id);

        // Subscribe to own Stream
        await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                try
                {
                    Console.WriteLine($"ProtoActor {Id} received event {envelope.Id} from stream");
                    Logger.LogDebug("ProtoActor {AgentId} received event {EventId} from stream", Id, envelope.Id);
                    // Directly handle the event instead of routing it again to avoid infinite loop
                    // RouteEventAsync would call SendToSelfAsync which produces to stream again
                    await Agent.HandleEventAsync(envelope, ct);
                    Logger.LogDebug("ProtoActor {AgentId} handled event {EventId}", Id, envelope.Id);
                    Console.WriteLine($"ProtoActor {Id} handled event {envelope.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ProtoActor {Id} ERROR handling event: {ex.Message}");
                    Logger.LogError(ex, "Error handling event {EventId}", envelope.Id);
                    throw;
                }
            },
            null, // No filter needed for self stream
            ct);

        Logger.LogInformation("ProtoActorGAgentActor {Id} activated and subscribed to stream", Id);

        // Update active Actor count
        var count = Interlocked.Increment(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("Deactivating agent {AgentId}", Id);

        // Stop Proto.Actor
        _rootContext.Send(_actorPid, new Stop());

        // Remove Agent from Registry to allow subsequent recreation of Actor with same ID
        _streamRegistry.Remove(Id);

        Logger.LogDebug("Agent {AgentId} removed from registry", Id);

        // Update active Actor count
        var count = Interlocked.Decrement(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    // RPC: Inherited from GAgentActorBase (uses shared RpcInvoker)
}