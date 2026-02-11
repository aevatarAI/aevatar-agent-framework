using System.Collections.Concurrent;
using System.Linq;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit implementation of IMessageStream.
/// </summary>
public class MassTransitMessageStream : IMessageStream
{
    private readonly IBus _bus;
    private readonly IServiceProvider _serviceProvider;
    private readonly MassTransitStreamOptions _options;
    private readonly ILogger<MassTransitMessageStream> _logger;
    private readonly ConcurrentDictionary<Guid, Func<EventEnvelope, Task>> _handlers = new();
    private readonly string? _category;

    /// <inheritdoc />
    public string StreamId { get; }

    public MassTransitMessageStream(
        string streamId,
        string? category,
        IBus bus,
        IServiceProvider serviceProvider,
        IOptions<MassTransitStreamOptions> options)
    {
        StreamId = streamId;
        _category = category;
        _bus = bus;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = serviceProvider.GetRequiredService<ILogger<MassTransitMessageStream>>();
    }

    /// <inheritdoc />
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            using var stream = new MemoryStream();
            envelope.WriteTo(stream);
            
            var payload = new ByteArrayMessage 
            { 
                StreamId = StreamId,
                Data = stream.ToArray() 
            };

            if (_options.TransportType == MassTransitTransportType.Kafka)
            {
                // Determine Topic
                string topic = _options.TopicPrefix;
                if (!string.IsNullOrEmpty(_category) && _options.TopicMapping.TryGetValue(_category, out var mappedTopic))
                {
                    topic = mappedTopic;
                }

                try 
                {
                    // ITopicProducerProvider is a scoped service, need to create a scope in Orleans Grain context
                    using var scope = _serviceProvider.CreateScope();
                    var producerProvider = scope.ServiceProvider.GetRequiredService<ITopicProducerProvider>();
                    var producer = producerProvider.GetProducer<string, ByteArrayMessage>(new Uri($"topic:{topic}"));
                    // Use StreamId as Key to ensure partition ordering
                    await producer.Produce(StreamId, payload, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to produce stream message to Kafka topic {Topic} (StreamId={StreamId})",
                        topic, StreamId);
                    throw;
                }
            }
            else if (_options.TransportType == MassTransitTransportType.InMemory)
            {
                // InMemory mode: Use Publish
                await _bus.Publish(payload, ct);
            }
            else
            {
                // RabbitMQ: Use Publish (Exchange routing)
                await _bus.Publish(payload, ct);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"MassTransitMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }

    /// <inheritdoc />
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        CancellationToken ct = default) where T : IMessage
    {
        return SubscribeAsync(handler, null, ct);
    }

    /// <inheritdoc />
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        var subscriptionId = Guid.NewGuid();
        
        Func<EventEnvelope, Task> wrapperHandler = async (envelope) =>
        {
            if (envelope is T typedMsg)
            {
                if (filter == null || filter(typedMsg))
                {
                    await handler(typedMsg);
                }
            }
        };

        _handlers.TryAdd(subscriptionId, wrapperHandler);
        
        // Log with explicit format to show actual value (not Serilog-quoted)
        _logger.LogInformation("[MassTransitMessageStream] Handler REGISTERED - StreamId='{StreamId}', StreamIdLength={Length}, SubscriptionId={SubscriptionId}, HandlerType={HandlerType}, TotalHandlers={Total}",
            StreamId, StreamId?.Length ?? 0, subscriptionId, typeof(T).Name, _handlers.Count);
        
        // Also log raw bytes to detect hidden characters
        if (!string.IsNullOrEmpty(StreamId))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(StreamId);
            var hex = string.Join(" ", bytes.Take(50).Select(b => b.ToString("X2")));
            _logger.LogDebug("[MassTransitMessageStream] StreamId raw bytes (first 50): {Hex}", hex);
        }

        return Task.FromResult<IMessageStreamSubscription>(
            new MassTransitMessageStreamSubscription(
                subscriptionId, 
                StreamId, 
                () => {
                    _handlers.TryRemove(subscriptionId, out _);
                    _logger.LogDebug("[MassTransitMessageStream] Handler UNREGISTERED - StreamId={StreamId}, SubscriptionId={SubscriptionId}, RemainingHandlers={Remaining}",
                        StreamId, subscriptionId, _handlers.Count);
                    return Task.CompletedTask;
                }));
    }

    /// <summary>
    /// Dispatches a received message to local subscribers.
    /// </summary>
    internal async Task DispatchAsync(byte[] data)
    {
        try 
        {
            var envelope = EventEnvelope.Parser.ParseFrom(data);
            var tasks = new List<Task>();
            foreach (var handler in _handlers.Values)
            {
                tasks.Add(handler(envelope));
            }
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error dispatching message: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the number of registered handlers (for debugging).
    /// </summary>
    internal int GetHandlerCount() => _handlers.Count;
}