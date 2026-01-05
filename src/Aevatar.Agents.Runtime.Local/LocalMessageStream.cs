using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime Message Stream implementation
/// Provides high-performance message queue based on System.Threading.Channels
/// </summary>
public class LocalMessageStream : IMessageStream
{
    private readonly Channel<EventEnvelope> _channel;
    private readonly ConcurrentDictionary<Guid, LocalMessageStreamSubscription> _subscriptions = new();
    private readonly CancellationTokenSource _cts = new();

    public string StreamId { get; }

    public LocalMessageStream(string streamId, int capacity = 1000)
    {
        StreamId = streamId;
        _channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Start message processing loop
        _ = ProcessMessagesAsync();
    }

    /// <summary>
    /// Publish message to Stream
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            await _channel.Writer.WriteAsync(envelope, ct);
        }
        else
        {
            throw new InvalidOperationException(
                $"LocalMessageStream only supports EventEnvelope, got {typeof(T).Name}");
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
        var subscriptionId = Guid.NewGuid();
        Func<EventEnvelope, Task> envelopeHandler;
        
        if (typeof(T) == typeof(EventEnvelope))
        {
            envelopeHandler = async env =>
            {
                var typedMessage = (T)(object)env;
                if (filter != null && !filter(typedMessage))
                {
                    return;
                }
                await handler(typedMessage);
            };
        }
        else
        {
            // Only subscribe to specific type events (filtered by Payload type URL)
            var expectedTypeUrl = $"type.googleapis.com/{typeof(T).FullName}";
            envelopeHandler = async env =>
            {
                if (env.Payload != null && env.Payload.TypeUrl.Contains(typeof(T).Name))
                {
                    try
                    {
                        // Use reflection to Unpack
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(typeof(T));

                        if (unpackMethod != null)
                        {
                            var message = (T)unpackMethod.Invoke(env.Payload, null)!;
                            if (filter != null && !filter(message))
                            {
                                return;
                            }
                            await handler(message);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore type mismatch events
                    }
                }
            };
        }
        
        var subscription = new LocalMessageStreamSubscription(
            subscriptionId,
            StreamId,
            envelopeHandler,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        return Task.FromResult<IMessageStreamSubscription>(subscription);
    }

    /// <summary>
    /// Message processing loop
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            // Concurrently call all active subscribers
            var tasks = _subscriptions.Values
                .Where(sub => sub.IsActive)
                .Select(subscription =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await subscription.HandleMessageAsync(envelope);
                        }
                        catch (Exception)
                        {
                            // Ignore subscriber errors, don't affect other subscribers
                        }
                    }));

            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Stop Stream
    /// </summary>
    public void Stop()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
    }
}