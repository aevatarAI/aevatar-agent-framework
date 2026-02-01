using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

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
    private static readonly ILogger? _staticLogger;
    private static int _traceEnqueueLogCount;
    private static int _traceDispatchLogCount;
    private static int _traceDispatchDoneLogCount;
    private static int _traceAssistantLogCount;

    public string StreamId { get; }

    static LocalMessageStream()
    {
        // Best-effort static logger initialization
        try
        {
            _staticLogger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug))
                .CreateLogger<LocalMessageStream>();
        }
        catch { /* ignore */ }
    }

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
            var isExecutionTrace = TryExtractExecutionTrace(envelope, out var executionId, out var nodeId, out var assistantLen, out var traceTsMs);
            var envelopeTsMs = ToUnixMs(envelope.Timestamp);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _channel.Writer.WriteAsync(envelope, ct);
            sw.Stop();
            if (isExecutionTrace)
            {
                var shouldLog = sw.ElapsedMilliseconds > 200 || Interlocked.Increment(ref _traceEnqueueLogCount) <= 3;
                if (shouldLog)
                {
                    #region agent log
                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        $"{{\"sessionId\":\"\",\"runId\":\"{executionId}\",\"hypothesisId\":\"H47\",\"location\":\"LocalMessageStream.cs:ProduceAsync\",\"message\":\"trace_enqueued\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"nodeId\":\"{nodeId}\",\"assistantLen\":{assistantLen},\"envelopeTsMs\":{envelopeTsMs},\"traceTsMs\":{traceTsMs},\"writeMs\":{sw.ElapsedMilliseconds}}},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n");
                    #endregion
                }
            }
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
            var expectedTypeName = typeof(T).Name;
            envelopeHandler = async env =>
            {
                var actualTypeUrl = env.Payload?.TypeUrl ?? "(null)";
                var matches = env.Payload != null && actualTypeUrl.Contains(expectedTypeName, StringComparison.OrdinalIgnoreCase);
                
                _staticLogger?.LogDebug("[LocalMessageStream] StreamId={StreamId} checking envelope: Expected={Expected}, Actual={Actual}, Matches={Matches}",
                    StreamId, expectedTypeName, actualTypeUrl, matches);

                if (env.Payload != null && matches)
                {
                    try
                    {
                        // Use reflection to Unpack
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", System.Type.EmptyTypes)
                            ?.MakeGenericMethod(typeof(T));

                        if (unpackMethod != null)
                        {
                            var message = (T)unpackMethod.Invoke(env.Payload, null)!;
                            _staticLogger?.LogDebug("[LocalMessageStream] Unpack successful for {TypeName}", expectedTypeName);
                            if (filter != null && !filter(message))
                            {
                                return;
                            }
                            await handler(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _staticLogger?.LogDebug(ex, "[LocalMessageStream] Unpack failed for {TypeName}", expectedTypeName);
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
        #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            $"{{\"sessionId\":\"\",\"runId\":\"\",\"hypothesisId\":\"H44\",\"location\":\"LocalMessageStream.cs:SubscribeAsync\",\"message\":\"subscription_created\",\"data\":{{\"streamId\":\"{StreamId}\",\"subscriptionId\":\"{subscriptionId}\",\"typeName\":\"{typeof(T).Name}\",\"hasFilter\":{(filter != null).ToString().ToLowerInvariant()}}},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n");
        #endregion
        return Task.FromResult<IMessageStreamSubscription>(subscription);
    }

    /// <summary>
    /// Message processing loop
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            var typeUrl = envelope.Payload?.TypeUrl ?? "(null)";
            var isExecutionTrace = typeUrl.Contains("ExecutionTraceEvent", StringComparison.OrdinalIgnoreCase);
            string executionId = string.Empty;
            string nodeId = string.Empty;
            int assistantLen = 0;
            long traceTsMs = 0;
            var envelopeTsMs = ToUnixMs(envelope.Timestamp);
            var activeCount = _subscriptions.Values.Count(s => s.IsActive);
            _staticLogger?.LogDebug("[LocalMessageStream] StreamId={StreamId} dispatching EventId={EventId} TypeUrl={TypeUrl} to {Count} active subs",
                StreamId, envelope.Id, typeUrl, activeCount);
            if (isExecutionTrace)
            {
                TryExtractExecutionTrace(envelope, out executionId, out nodeId, out assistantLen, out traceTsMs);
                if (assistantLen > 0 && Interlocked.Increment(ref _traceAssistantLogCount) <= 3)
                {
                    var nowMsAssist = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    #region agent log
                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        $"{{\"sessionId\":\"\",\"runId\":\"{executionId}\",\"hypothesisId\":\"H54\",\"location\":\"LocalMessageStream.cs:ProcessMessagesAsync\",\"message\":\"trace_assistant_in_stream\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"nodeId\":\"{nodeId}\",\"assistantLen\":{assistantLen},\"envelopeTsMs\":{envelopeTsMs},\"traceTsMs\":{traceTsMs}}},\"timestamp\":{nowMsAssist}}}\n");
                    #endregion
                }
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var queueWaitMs = envelopeTsMs > 0 ? Math.Max(0, nowMs - envelopeTsMs) : -1;
                var shouldLog = queueWaitMs > 1000 || Interlocked.Increment(ref _traceDispatchLogCount) <= 3;
                if (shouldLog)
                {
                    #region agent log
                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        $"{{\"sessionId\":\"\",\"runId\":\"{executionId}\",\"hypothesisId\":\"H48\",\"location\":\"LocalMessageStream.cs:ProcessMessagesAsync\",\"message\":\"trace_queue_wait\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"nodeId\":\"{nodeId}\",\"assistantLen\":{assistantLen},\"queueWaitMs\":{queueWaitMs},\"envelopeTsMs\":{envelopeTsMs},\"traceTsMs\":{traceTsMs}}},\"timestamp\":{nowMs}}}\n");
                    #endregion
                }
                #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    $"{{\"sessionId\":\"\",\"runId\":\"\",\"hypothesisId\":\"H45\",\"location\":\"LocalMessageStream.cs:ProcessMessagesAsync\",\"message\":\"dispatch_start\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"typeUrl\":\"{typeUrl}\",\"activeCount\":{activeCount}}},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n");
                #endregion
            }

            // Dispatch to all active subscribers.
            var tasks = _subscriptions.Values
                .Where(sub => sub.IsActive)
                .Select(async subscription =>
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await subscription.HandleMessageAsync(envelope);
                        sw.Stop();
                        if (isExecutionTrace && sw.ElapsedMilliseconds > 200)
                        {
                            #region agent log
                            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                $"{{\"sessionId\":\"\",\"runId\":\"\",\"hypothesisId\":\"H46\",\"location\":\"LocalMessageStream.cs:ProcessMessagesAsync\",\"message\":\"dispatch_slow_sub\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"typeUrl\":\"{typeUrl}\",\"subscriptionId\":\"{subscription.SubscriptionId}\",\"elapsedMs\":{sw.ElapsedMilliseconds}}},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n");
                            #endregion
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore subscriber errors
                    }
                });

            System.Diagnostics.Stopwatch? dispatchSw = null;
            if (isExecutionTrace)
            {
                dispatchSw = System.Diagnostics.Stopwatch.StartNew();
            }
            await Task.WhenAll(tasks);
            if (isExecutionTrace && dispatchSw != null)
            {
                dispatchSw.Stop();
                var shouldLog = dispatchSw.ElapsedMilliseconds > 200 || Interlocked.Increment(ref _traceDispatchDoneLogCount) <= 3;
                if (shouldLog)
                {
                    #region agent log
                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        $"{{\"sessionId\":\"\",\"runId\":\"{executionId}\",\"hypothesisId\":\"H49\",\"location\":\"LocalMessageStream.cs:ProcessMessagesAsync\",\"message\":\"dispatch_done\",\"data\":{{\"streamId\":\"{StreamId}\",\"eventId\":\"{envelope.Id}\",\"nodeId\":\"{nodeId}\",\"assistantLen\":{assistantLen},\"dispatchMs\":{dispatchSw.ElapsedMilliseconds}}},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}\n");
                    #endregion
                }
            }
        }
    }

    private static bool TryExtractExecutionTrace(
        EventEnvelope envelope,
        out string executionId,
        out string nodeId,
        out int assistantLen,
        out long traceTsMs)
    {
        executionId = string.Empty;
        nodeId = string.Empty;
        assistantLen = 0;
        traceTsMs = 0;

        var typeUrl = envelope.Payload?.TypeUrl ?? string.Empty;
        if (!typeUrl.Contains("ExecutionTraceEvent", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var trace = envelope.Payload!.Unpack<ExecutionTraceEvent>();
            nodeId = (trace.NodeId ?? string.Empty).Trim();
            executionId = ReadStringField(trace, ExecutionTraceEventFields.ExecutionId) ?? string.Empty;
            var assistant = ReadStringField(trace, ExecutionTraceEventFields.AssistantResponse);
            assistantLen = assistant?.Length ?? 0;
            traceTsMs = ToUnixMs(trace.Timestamp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadStringField(ExecutionTraceEvent evt, string key)
    {
        if (!evt.Fields.TryGetValue(key, out var value))
            return null;

        return value.ValueCase switch
        {
            ContextValue.ValueOneofCase.StringValue => value.StringValue,
            ContextValue.ValueOneofCase.GuidString => value.GuidString,
            ContextValue.ValueOneofCase.DatetimeIso => value.DatetimeIso,
            ContextValue.ValueOneofCase.IntValue => value.IntValue.ToString(),
            ContextValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString("G"),
            ContextValue.ValueOneofCase.BoolValue => value.BoolValue.ToString(),
            _ => null
        };
    }

    private static long ToUnixMs(Timestamp? ts)
    {
        if (ts == null)
            return 0;

        try
        {
            var dt = ts.ToDateTime();
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }
        catch
        {
            return 0;
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
