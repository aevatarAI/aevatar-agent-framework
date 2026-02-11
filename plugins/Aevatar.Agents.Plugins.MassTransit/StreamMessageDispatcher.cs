using System.Linq;
using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit consumer that dispatches incoming messages to agents.
/// 
/// Key design: Uses IMassTransitEventHandler to properly dispatch events to actors.
/// This ensures callbacks run in the correct actor context (e.g., Grain turn for Orleans).
/// 
/// Dispatch modes (configurable via Consumer.DispatchMode):
/// - GrainOnly: Only dispatch to Grain handlers (Silo, best performance)
/// - LocalStreamOnly: Only dispatch to local memory streams (HttpApi Client)
/// - Both: Dispatch to both (for debugging)
/// </summary>
public class StreamMessageDispatcher : IConsumer<ByteArrayMessage>
{
    private readonly IEnumerable<IMassTransitEventHandler> _eventHandlers;
    private readonly IEnumerable<IStreamNotFoundHandler> _notFoundHandlers;
    private readonly IEnumerable<ITraceIdExtractor> _traceIdExtractors;
    private readonly ILogger<StreamMessageDispatcher> _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly DispatchHandler _dispatchHandler;

    public StreamMessageDispatcher(
        IEnumerable<IMassTransitEventHandler> eventHandlers,
        IEnumerable<IStreamNotFoundHandler> notFoundHandlers,
        ILogger<StreamMessageDispatcher> logger,
        IEnumerable<ITraceIdExtractor>? traceIdExtractors = null,
        IOptions<MassTransitStreamOptions>? options = null,
        IServiceProvider? serviceProvider = null)
    {
        _eventHandlers = eventHandlers;
        _notFoundHandlers = notFoundHandlers;
        _traceIdExtractors = traceIdExtractors ?? Enumerable.Empty<ITraceIdExtractor>();
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dispatchHandler = options?.Value?.Consumer?.DispatchHandler ?? DispatchHandler.GrainHandler;
    }

    public async Task Consume(ConsumeContext<ByteArrayMessage> context)
    {
        var streamId = context.Message.StreamId;
        var data = context.Message.Data;
        
        // Skip warmup messages (used for pre-establishing Kafka connections)
        if (string.IsNullOrEmpty(streamId))
        {
            _logger.LogDebug("Skipping warmup message");
            return;
        }
        
        // ============================================================
        // EARLY FILTER: For LocalHandler (broadcast mode), check if we have
        // a local subscriber BEFORE any heavy processing (deserialization, reflection).
        // This is O(1) lookup - avoids wasting resources on irrelevant messages.
        // ============================================================
        if (_dispatchHandler == DispatchHandler.LocalHandler)
        {
            var streamProvider = _serviceProvider?.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider == null || !streamProvider.HasSubscriber(streamId))
            {
                // No local subscriber - skip immediately without heavy processing
                // Silent return - this is expected in broadcast mode
                return;
            }
        }
        
        // === Proceed with heavy processing only for relevant messages ===
        
        // Parse the envelope
        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Parser.ParseFrom(data);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "[StreamMessageDispatcher] Failed to parse EventEnvelope for StreamId {StreamId}", streamId);
            throw;
        }
        
        // Extract TraceId via pluggable extractors (application-specific)
        var traceId = ExtractTraceId(envelope);
        var traceIdPrefix = traceId != null ? $"[TraceId={traceId}]" : "";
        
        // Defensive: Strip quotes from StreamId if present
        streamId = SanitizeStreamId(streamId, traceIdPrefix);
        
        _logger.LogDebug(
            "[StreamMessageDispatcher]{TraceId} Consuming message - StreamId='{StreamId}', DispatchHandler={DispatchHandler}",
            traceIdPrefix, streamId, _dispatchHandler);
        
        // ============================================================
        // Dispatch based on configured handler type
        // ============================================================
        if (_dispatchHandler == DispatchHandler.LocalHandler)
        {
            await TryDispatchToLocalStreamAsync(streamId, data, envelope);
            return;
        }
        
        // GrainHandler: Dispatch to Grain handlers (Silo)
        var handled = await TryDispatchToGrainHandlersAsync(streamId, envelope);
        if (handled) return;
        
        // Try activation handlers and retry
        handled = await TryActivateAndRetryAsync(streamId, envelope);
        if (handled) return;
        
        // If still not handled, throw to trigger MassTransit retry
        _logger.LogWarning("Message dropped - StreamId={StreamId}, Reason=NoHandler (will retry)", streamId);
        throw new System.InvalidOperationException(
            $"No handler for StreamId {streamId}. Actor might be failing to activate.");
    }

    /// <summary>
    /// Extract trace ID from envelope using registered ITraceIdExtractor implementations.
    /// Returns null if no extractor matches.
    /// </summary>
    private string? ExtractTraceId(EventEnvelope envelope)
    {
        foreach (var extractor in _traceIdExtractors)
        {
            try
            {
                if (extractor.TryExtract(envelope, out var traceId) && !string.IsNullOrEmpty(traceId))
                    return traceId;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "[StreamMessageDispatcher] TraceIdExtractor {Extractor} failed for TypeUrl={TypeUrl}",
                    extractor.GetType().Name, envelope.Payload?.TypeUrl);
            }
        }
        return null;
    }

    /// <summary>
    /// Sanitize StreamId by stripping stray quotes from edge-case JSON serialization.
    /// </summary>
    private string SanitizeStreamId(string streamId, string traceIdPrefix)
    {
        if (string.IsNullOrEmpty(streamId)) return streamId;

        var stripped = streamId.Trim('"', '\'', '\u201C', '\u201D', ' ', '\t');
        if (stripped.Length != streamId.Length)
        {
            _logger.LogDebug(
                "[StreamMessageDispatcher]{TraceId} Stripped quotes from StreamId: '{Original}' -> '{Stripped}'",
                traceIdPrefix, streamId, stripped);
            return stripped;
        }
        return streamId;
    }
    
    /// <summary>
    /// Dispatch to local memory stream subscribers (e.g., ChatMiddleware in HttpApi)
    /// </summary>
    private async Task<bool> TryDispatchToLocalStreamAsync(
        string streamId, byte[] data, EventEnvelope envelope)
    {
        if (_serviceProvider == null)
        {
            _logger.LogWarning("Message dropped - StreamId={StreamId}, Reason=ServiceProviderNull", streamId);
            return false;
        }
        
        try
        {
            var streamProvider = _serviceProvider.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider == null)
            {
                _logger.LogWarning("Message dropped - StreamId={StreamId}, Reason=StreamProviderNotFound", streamId);
                return false;
            }
            
            var localStream = streamProvider.GetStreamInternal(streamId);
            if (localStream != null)
            {
                var handlerCount = localStream.GetHandlerCount();
                if (handlerCount == 0)
                {
                    _logger.LogWarning(
                        "Message dropped - StreamId={StreamId}, Reason=NoHandlers (race condition)",
                        streamId);
                    return false;
                }
                
                await localStream.DispatchAsync(data);
                _logger.LogInformation(
                    "Message dispatched - StreamId={StreamId}, Handlers={HandlerCount}",
                    streamId, handlerCount);
                return true;
            }
            else
            {
                _logger.LogWarning("Message dropped - StreamId={StreamId}, Reason=StreamNotFound", streamId);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch to local stream for StreamId {StreamId}", streamId);
        }
        
        return false;
    }
    
    /// <summary>
    /// Dispatch to Grain handlers (Orleans/ProtoActor actors)
    /// </summary>
    private async Task<bool> TryDispatchToGrainHandlersAsync(
        string streamId, EventEnvelope envelope)
    {
        foreach (var handler in _eventHandlers)
        {
            try
            {
                var handled = await handler.HandleEventAsync(streamId, envelope);
                if (handled)
                {
                    _logger.LogInformation(
                        "Message dispatched - StreamId={StreamId}, Handler={HandlerType}", 
                        streamId, handler.GetType().Name);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex,
                    "Handler failed - StreamId={StreamId}, Handler={HandlerType}", 
                    streamId, handler.GetType().Name);
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Try stream not found handlers (activate actor) and retry dispatch
    /// </summary>
    private async Task<bool> TryActivateAndRetryAsync(
        string streamId, EventEnvelope envelope)
    {
        _logger.LogDebug("Trying activation for StreamId={StreamId}", streamId);
        
        foreach (var notFoundHandler in _notFoundHandlers)
        {
            try
            {
                await notFoundHandler.HandleStreamNotFoundAsync(streamId);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Activation failed - StreamId={StreamId}", streamId);
            }
        }
        
        // Retry with event handlers after activation
        foreach (var handler in _eventHandlers)
        {
            try
            {
                var handled = await handler.HandleEventAsync(streamId, envelope);
                if (handled)
                {
                    _logger.LogInformation(
                        "Message dispatched (after activation) - StreamId={StreamId}", streamId);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex,
                    "Handler failed (after activation) - StreamId={StreamId}", streamId);
            }
        }
        
        return false;
    }
}
