using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local.Subscription;

/// <summary>
/// Local runtime subscription manager implementation
/// </summary>
public class LocalSubscriptionManager : BaseSubscriptionManager
{
    private readonly LocalMessageStreamRegistry _streamRegistry;
    // Store original event handlers to support reconnection
    private readonly Dictionary<string, Func<EventEnvelope, Task>> _eventHandlers = new();
    
    public LocalSubscriptionManager(
        LocalMessageStreamRegistry streamRegistry,
        ILogger<LocalSubscriptionManager>? logger = null)
        : base(logger)
    {
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating Local stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        // Get parent node's stream (don't auto-create, so retry is triggered when stream doesn't exist)
        var parentStream = _streamRegistry.GetStream(parentId);
        
        if (parentStream == null)
        {
            throw new InvalidOperationException($"Cannot get stream for parent {parentId}");
        }
        
        // Create subscription, wrap event handler to add error handling
        var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
        
        // Store original event handler to support reconnection (using wrapped handler)
        _eventHandlers[childId] = wrappedHandler;
        
        // Create filter (optional)
        Func<EventEnvelope, bool>? filter = envelope =>
        {
            // Filter out events published by child node itself to avoid loops
            if (envelope.PublisherId == childId)
            {
                Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                    envelope.Id, childId);
                return false;
            }
            
            // Other filter logic...
            return true;
        };
        
        // Subscribe to parent node's stream
        var subscription = await parentStream.SubscribeAsync<EventEnvelope>(
            wrappedHandler, 
            filter, 
            cancellationToken);
        
        Logger.LogInformation("Successfully created Local stream subscription for Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        return subscription;
    }

    protected override async Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription)
    {
        if (subscription?.StreamSubscription == null)
        {
            return false;
        }
        
        // For Local runtime, check if stream is still in registry
        var parentStream = _streamRegistry.GetStream(subscription.ParentId);
        if (parentStream == null)
        {
            Logger.LogWarning("Parent stream {ParentId} not found in registry", subscription.ParentId);
            return false;
        }
        
        // Check if subscription is still active
        // More health check logic can be added here
        var isHealthy = subscription.StreamSubscription != null;
        
        if (!isHealthy)
        {
            Logger.LogWarning("Subscription {SubscriptionId} is unhealthy", subscription.SubscriptionId);
        }
        
        return isHealthy;
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting Local stream subscription {SubscriptionId}",
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
                Logger.LogWarning(ex, "Error cleaning up old subscription during reconnect");
            }
        }
        
        // Re-acquire parent stream
        var parentStream = _streamRegistry.GetOrCreateStream(handle.ParentId);
        
        // Get saved event handler
        if (!_eventHandlers.TryGetValue(handle.ChildId, out var wrappedHandler))
        {
            throw new InvalidOperationException(
                $"Event handler for child {handle.ChildId} not found. Cannot reconnect.");
        }
        
        // Create filter (same as when creating)
        Func<EventEnvelope, bool>? filter = envelope =>
        {
            if (envelope.PublisherId == handle.ChildId)
            {
                Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                    envelope.Id, handle.ChildId);
                return false;
            }
            return true;
        };
        
        // Re-subscribe
        var newSubscription = await parentStream.SubscribeAsync<EventEnvelope>(
            wrappedHandler,
            filter,
            cancellationToken);
        
        // Update handle
        handle.StreamSubscription = newSubscription;
        handle.IsHealthy = true;
        handle.LastActivityAt = DateTime.UtcNow;
        
        Logger.LogInformation("Successfully reconnected subscription {SubscriptionId}", 
            handle.SubscriptionId);
    }

    /// <summary>
    /// Clean up saved event handler
    /// </summary>
    public void CleanupEventHandler(string childId)
    {
        if (_eventHandlers.Remove(childId))
        {
            Logger.LogDebug("Cleaned up event handler for child {ChildId}", childId);
        }
    }

    /// <summary>
    /// Create wrapped event handler, add error handling and logging
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
                Logger.LogTrace("Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                await originalHandler(envelope);
                
                // Update subscription activity time
                UpdateLastActivity(childId, parentId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // Can choose whether to rethrow exception
                // throw;
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
    /// Create subscription with health monitoring
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeWithHealthMonitoringAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        TimeSpan healthCheckInterval,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        // Start health monitoring task
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(healthCheckInterval, cancellationToken);
                    
                    var isHealthy = await IsSubscriptionHealthyAsync(subscription);
                    
                    if (!isHealthy)
                    {
                        Logger.LogWarning("Subscription {SubscriptionId} is unhealthy, attempting to recover",
                            subscription.SubscriptionId);
                        
                        // Can trigger reconnection or other recovery operations here
                        // await ReconnectSubscriptionAsync(subscription, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in health monitoring for subscription {SubscriptionId}",
                        subscription.SubscriptionId);
                }
            }
        }, cancellationToken);
        
        return subscription;
    }
}
