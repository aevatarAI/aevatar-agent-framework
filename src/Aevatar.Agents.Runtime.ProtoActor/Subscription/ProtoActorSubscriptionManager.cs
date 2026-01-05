using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor.Subscription;

/// <summary>
/// ProtoActor runtime subscription manager implementation
/// </summary>
public class ProtoActorSubscriptionManager : BaseSubscriptionManager
{
    private readonly IRootContext _rootContext;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly IGAgentActorManager _actorManager;
    
    public ProtoActorSubscriptionManager(
        IRootContext rootContext,
        ProtoActorMessageStreamRegistry streamRegistry,
        IGAgentActorManager actorManager,
        ILogger<ProtoActorSubscriptionManager>? logger = null)
        : base(logger)
    {
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating ProtoActor stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        try
        {
            // Get parent node's Actor PID
            var parentPid = await GetActorPidAsync(parentId);
            if (parentPid == null)
            {
                throw new InvalidOperationException($"Parent actor {parentId} not found in registry");
            }
            
            // Create ProtoActor message stream
            var messageStream = new ProtoActorMessageStream(parentId, parentPid, _rootContext);
            
            // Create filter
            Func<EventEnvelope, bool>? filter = envelope =>
            {
                // Filter out self-published events from child node to avoid loops
                if (envelope.PublisherId == childId)
                {
                    Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                        envelope.Id, childId);
                    return false;
                }
                
                // ProtoActor-specific: Check message routing
                // ProtoActor messages are sent directly, unlike Orleans which has stream broadcasting
                // So need to be particularly careful to avoid loops
                if (envelope.Direction == EventDirection.Both)
                {
                    if (envelope.Publishers.Contains(parentId))
                    {
                        Logger.LogTrace("BOTH event {EventId} from parent, will be converted to DOWN-only",
                            envelope.Id);
                    }
                }
                
                return true;
            };
            
            // Wrap event handler, add ProtoActor-specific processing logic
            var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
            
            // Create subscription
            var subscription = await messageStream.SubscribeAsync<EventEnvelope>(
                wrappedHandler,
                filter,
                cancellationToken);
            
            Logger.LogInformation(
                "Successfully created ProtoActor stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "Failed to create ProtoActor stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            throw;
        }
    }

    protected override async Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription)
    {
        if (subscription?.StreamSubscription == null)
        {
            return false;
        }
        
        // ProtoActor subscription health status
        if (subscription.StreamSubscription is ProtoActorStreamSubscription protoSubscription)
        {
            var isHealthy = protoSubscription.IsActive;
            
            // Additional check: Verify if target Actor still exists
            if (isHealthy)
            {
                var parentPid = await GetActorPidAsync(subscription.ParentId);
                if (parentPid == null)
                {
                    Logger.LogWarning(
                        "Parent actor {ParentId} not found for subscription {SubscriptionId}",
                        subscription.ParentId, subscription.SubscriptionId);
                    return false;
                }
                
                // Can send ping message to verify Actor responsiveness
                try
                {
                    var response = await _rootContext.RequestAsync<PingResponse>(
                        parentPid,
                        new PingMessage(),
                        CancellationToken.None);
                    
                    isHealthy = response != null;
                }
                catch
                {
                    isHealthy = false;
                }
            }
            
            if (!isHealthy)
            {
                Logger.LogWarning("ProtoActor subscription {SubscriptionId} is unhealthy", 
                    subscription.SubscriptionId);
            }
            
            return isHealthy;
        }
        
        // Default to unhealthy
        return false;
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting ProtoActor stream subscription {SubscriptionId}",
            handle.SubscriptionId);
        
        // Clean up old subscription
        if (handle.StreamSubscription != null)
        {
            try
            {
                await handle.StreamSubscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error cleaning up old ProtoActor subscription during reconnect");
            }
        }
        
        // ProtoActor reconnection strategy:
        // 1. First try Resume (if subscription object still exists)
        // 2. If Actor has restarted or doesn't exist, need to recreate
        
        if (handle.StreamSubscription is ProtoActorStreamSubscription protoSubscription)
        {
            try
            {
                // Check if parent Actor still exists
                var parentPid = await GetActorPidAsync(handle.ParentId);
                if (parentPid != null)
                {
                    // Actor exists, try to resume subscription
                    await protoSubscription.ResumeAsync();
                    handle.IsHealthy = true;
                    handle.LastActivityAt = DateTime.UtcNow;
                    
                    Logger.LogInformation("Successfully resumed ProtoActor subscription {SubscriptionId}",
                        handle.SubscriptionId);
                    return;
                }
                else
                {
                    Logger.LogWarning("Parent actor {ParentId} not found, cannot resume subscription",
                        handle.ParentId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to resume ProtoActor subscription");
            }
        }
        
        // Cannot resume, throw exception
        throw new NotImplementedException(
            "Full reconnection requires saving the original event handler. " +
            "ProtoActor subscriptions are in-memory only and cannot be fully recreated without the handler.");
    }

    /// <summary>
    /// Create wrapped event handler, add ProtoActor-specific processing logic
    /// </summary>
    private Func<EventEnvelope, Task> CreateWrappedEventHandler(
        Func<EventEnvelope, Task> originalHandler,
        string childId,
        string parentId)
    {
        return async (EventEnvelope envelope) =>
        {
            try
            {
                Logger.LogTrace("ProtoActor Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                // ProtoActor-specific: Handle BOTH direction events
                // If receiving BOTH event from parent node, need to convert to DOWN-only
                if (envelope.Direction == EventDirection.Both && 
                    envelope.Publishers.Contains(parentId))
                {
                    Logger.LogDebug(
                        "Converting BOTH event {EventId} to DOWN-only for ProtoActor child {ChildId}",
                        envelope.Id, childId);
                    
                    // Create modified envelope
                    var modifiedEnvelope = envelope.Clone();
                    modifiedEnvelope.Direction = EventDirection.Down;
                    
                    // Call handler with modified envelope
                    await originalHandler(modifiedEnvelope);
                }
                else
                {
                    // Other cases call original handler directly
                    await originalHandler(envelope);
                }
                
                // Update subscription activity time
                UpdateLastActivity(childId, parentId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "ProtoActor: Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // ProtoActor error handling: Log error but continue processing
                // Don't rethrow exception to avoid affecting Actor message processing
            }
        };
    }

    /// <summary>
    /// Update last activity time
    /// </summary>
    private void UpdateLastActivity(string childId, string parentId)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.ChildId == childId && subscription.ParentId == parentId)
            {
                subscription.LastActivityAt = DateTime.UtcNow;
                break;
            }
        }
    }

    /// <summary>
    /// Get Actor PID from manager
    /// </summary>
    private async Task<PID?> GetActorPidAsync(string actorId)
    {
        // Get Actor from manager
        var actor = await _actorManager.GetActorAsync(actorId);
        if (actor is ProtoActorGAgentActor protoActor)
        {
            // ProtoActorGAgentActor already provides GetPid() method
            return protoActor.GetPid();
        }
        
        // Can also get PID directly from stream registry
        var pid = _streamRegistry.GetPid(actorId);
        return pid;
    }

    /// <summary>
    /// Create direct subscription between Actors (ProtoActor-specific)
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeDirectAsync(
        PID parentPid,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // ProtoActor supports direct subscription via PID, no need to lookup by Guid
        // This can provide better performance
        
        var parentId = Guid.NewGuid().ToString(); // Generate a temporary ID
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        return subscription;
    }

    /// <summary>
    /// Batch create subscriptions (optimized version)
    /// </summary>
    public async Task<IReadOnlyList<ISubscriptionHandle>> SubscribeBatchOptimizedAsync(
        IReadOnlyList<(string ParentId, string ChildId)> subscriptions,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // ProtoActor can batch send messages, optimize batch subscriptions
        var results = new List<ISubscriptionHandle>();
        
        foreach (var (parentId, childId) in subscriptions)
        {
            try
            {
                var subscription = await SubscribeWithRetryAsync(
                    parentId, childId, eventHandler, retryPolicy, cancellationToken);
                results.Add(subscription);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, 
                    "Failed to create subscription for Child {ChildId} -> Parent {ParentId}",
                    childId, parentId);
                // Continue processing other subscriptions
            }
        }
        
        return results;
    }
}

/// <summary>
/// Ping message for health check
/// </summary>
internal class PingMessage { }

/// <summary>
/// Ping response
/// </summary>
internal class PingResponse 
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
