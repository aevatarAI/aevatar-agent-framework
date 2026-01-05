using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor runtime Message Stream implementation
/// Implements Stream semantics based on Actor message passing
/// </summary>
public class ProtoActorMessageStream : IMessageStream
{
    private readonly PID _targetPid;
    private readonly IRootContext _rootContext;
    private readonly ConcurrentDictionary<Guid, ProtoActorStreamSubscription> _subscriptions = new();

    public string StreamId { get; }

    public ProtoActorMessageStream(string streamId, PID targetPid, IRootContext rootContext)
    {
        StreamId = streamId;
        _targetPid = targetPid;
        _rootContext = rootContext;
    }

    /// <summary>
    /// Publish message to Stream (via Actor message passing)
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            // Send HandleEventMessage to target Actor
            _rootContext.Send(_targetPid, new HandleEventMessage { Envelope = envelope });
            
            // Trigger all subscribed handlers
            var tasks = new List<Task>();
            Console.WriteLine($"ProtoActorMessageStream {StreamId} producing event, subscriptions count: {_subscriptions.Count}");
            foreach (var subscription in _subscriptions.Values)
            {
                if (subscription.IsActive)
                {
                    Console.WriteLine($"ProtoActorMessageStream {StreamId} invoking subscription {subscription.SubscriptionId}");
                    tasks.Add(subscription.HandleMessageAsync(envelope));
                }
            }
            
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"ProtoActorMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }

    /// <summary>
    /// Subscribe to Stream messages
    /// </summary>
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) where T : IMessage
    {
        return SubscribeAsync(handler, filter: null, ct);
    }
    
    /// <summary>
    /// Subscribe to Stream messages (with filter)
    /// </summary>
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        // In Proto.Actor, message processing is done by Actor itself
        // Here we only need to create a subscription handle for management
        var subscriptionId = Guid.NewGuid();
        
        // Convert to IMessage type handler and filter
        Func<IMessage, Task> messageHandler = async (msg) =>
        {
            if (msg is T typedMsg)
            {
                await handler(typedMsg);
            }
        };
        
        Func<IMessage, bool>? messageFilter = null;
        if (filter != null)
        {
            messageFilter = (msg) => msg is T typedMsg && filter(typedMsg);
        }
        
        var subscription = new ProtoActorStreamSubscription(
            subscriptionId,
            StreamId,
            messageHandler,
            messageFilter,
            _targetPid,
            _rootContext,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        
        Console.WriteLine($"ProtoActorMessageStream {StreamId} added subscription {subscriptionId}, total subscriptions: {_subscriptions.Count}");
        
        // Proto.Actor's actual message processing is in Actor's Receive method
        // Subscription only records handler for later use
        return Task.FromResult<IMessageStreamSubscription>(subscription);
    }
}