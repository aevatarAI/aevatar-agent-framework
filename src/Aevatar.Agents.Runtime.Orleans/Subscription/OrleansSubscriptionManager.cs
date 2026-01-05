using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Subscription;

/// <summary>
/// Orleans runtime subscription manager implementation
/// </summary>
public class OrleansSubscriptionManager : BaseSubscriptionManager
{
    private readonly IStreamProvider _streamProvider;
    private readonly string _streamNamespace;
    
    public OrleansSubscriptionManager(
        IStreamProvider streamProvider,
        string streamNamespace = AevatarAgentsOrleansConstants.StreamNamespace,
        ILogger<OrleansSubscriptionManager>? logger = null)
        : base(logger)
    {
        _streamProvider = streamProvider ?? throw new ArgumentNullException(nameof(streamProvider));
        _streamNamespace = streamNamespace;
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating Orleans stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        try
        {
            // Get parent node's Orleans stream
            var streamId = StreamId.Create(_streamNamespace, parentId);
            var parentStream = _streamProvider.GetStream<byte[]>(streamId);
            
            // Wrap as OrleansMessageStream
            var messageStream = new OrleansMessageStream(parentId, parentStream);
            
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
                
                // Orleans-specific: Check if BOTH event received from parent stream
                // Need special handling to prevent loops
                if (envelope.Direction == EventDirection.Both)
                {
                    // If Publishers list already contains parent node, this is from parent stream
                    if (envelope.Publishers.Contains(parentId))
                    {
                        Logger.LogTrace("BOTH event {EventId} from parent stream, will be converted to DOWN-only",
                            envelope.Id);
                        // Note: Actual direction conversion should be done in handler
                    }
                }
                
                return true;
            };
            
            // Wrap event handler, add Orleans-specific processing logic
            var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
            
            // Create subscription
            var subscription = await messageStream.SubscribeAsync<EventEnvelope>(
                wrappedHandler,
                filter,
                cancellationToken);
            
            Logger.LogInformation(
                "Successfully created Orleans stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "Failed to create Orleans stream subscription for Child {ChildId} -> Parent {ParentId}",
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
        
        // Orleans subscription health status mainly judged by StreamSubscriptionHandle's IsActive property
        if (subscription.StreamSubscription is OrleansMessageStreamSubscription orleansSubscription)
        {
            var isHealthy = orleansSubscription.IsActive;
            
            if (!isHealthy)
            {
                Logger.LogWarning("Orleans subscription {SubscriptionId} is inactive", 
                    subscription.SubscriptionId);
            }
            
            return isHealthy;
        }
        
        // If not Orleans subscription type, try other ways to judge
        try
        {
            // Can try to get stream to verify connection
            var streamId = StreamId.Create(_streamNamespace, subscription.ParentId);
            var stream = _streamProvider.GetStream<byte[]>(streamId);
            
            // If stream can be successfully obtained, consider it healthy
            return stream != null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "Failed to check health for Orleans subscription {SubscriptionId}",
                subscription.SubscriptionId);
            return false;
        }
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting Orleans stream subscription {SubscriptionId}",
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
                Logger.LogWarning(ex, "Error cleaning up old Orleans subscription during reconnect");
            }
        }
        
        // Orleans reconnection strategy:
        // 1. Try to resume subscription using ResumeAsync
        // 2. If fails, recreate subscription
        
        if (handle.StreamSubscription is OrleansMessageStreamSubscription orleansSubscription)
        {
            try
            {
                // Try to resume subscription
                await orleansSubscription.ResumeAsync();
                handle.IsHealthy = true;
                handle.LastActivityAt = DateTime.UtcNow;
                
                Logger.LogInformation("Successfully resumed Orleans subscription {SubscriptionId}",
                    handle.SubscriptionId);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to resume Orleans subscription, will recreate");
            }
        }
        
        // If resume fails or not Orleans subscription, throw exception
        // Because we need original eventHandler to recreate subscription
        throw new NotImplementedException(
            "Full reconnection requires saving the original event handler. " +
            "Consider using ResumeAsync() for Orleans subscriptions or storing the handler in SubscriptionHandle.");
    }

    /// <summary>
    /// Create wrapped event handler, add Orleans-specific processing logic
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
                Logger.LogTrace("Orleans Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                // Orleans-specific: Handle BOTH direction events
                // If receiving BOTH event from parent stream, need to convert to DOWN-only
                if (envelope.Direction == EventDirection.Both && 
                    envelope.Publishers.Contains(parentId))
                {
                    Logger.LogDebug(
                        "Converting BOTH event {EventId} to DOWN-only for Orleans child {ChildId}",
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
                    "Orleans: Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // Orleans error handling strategy: Don't rethrow exception to avoid affecting stream
                // Error logged, continue processing subsequent events
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
    /// Create persistent subscription (Orleans supports persistent subscriptions)
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeWithPersistenceAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        string? subscriptionId = null,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // Orleans supports creating persistent subscriptions with specific subscriptionId
        // This allows subscription recovery after system restart
        
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        // TODO: Can save subscription info to Orleans storage
        // To support automatic recovery after system restart
        
        return subscription;
    }

    /// <summary>
    /// Batch create subscriptions
    /// </summary>
    public async Task<IReadOnlyList<ISubscriptionHandle>> SubscribeBatchAsync(
        IReadOnlyList<(string ParentId, string ChildId)> subscriptions,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = subscriptions.Select(s => 
            SubscribeWithRetryAsync(s.ParentId, s.ChildId, eventHandler, retryPolicy, cancellationToken));
        
        var results = await Task.WhenAll(tasks);
        return results;
    }
}

