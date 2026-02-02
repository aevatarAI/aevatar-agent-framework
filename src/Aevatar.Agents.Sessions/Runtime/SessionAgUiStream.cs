using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using AgUiToolCallStartEvent = Aevatar.Agents.AGUI.ToolCallStartEvent;
using AgUiToolCallEndEvent = Aevatar.Agents.AGUI.ToolCallEndEvent;
using CoreToolCallStartEvent = Aevatar.Agents.AI.Core.Messages.ToolCallStartEvent;
using CoreToolCallEndEvent = Aevatar.Agents.AI.Core.Messages.ToolCallEndEvent;
using CoreTextMessageStartEvent = Aevatar.Agents.AI.Core.Messages.TextMessageStartEvent;
using CoreTextMessageContentEvent = Aevatar.Agents.AI.Core.Messages.TextMessageContentEvent;
using CoreTextMessageEndEvent = Aevatar.Agents.AI.Core.Messages.TextMessageEndEvent;

namespace Aevatar.Agents.Sessions.Runtime;

// ============================================================
//  SessionAgUiStream - 极简版
//
//  职责：
//  - 订阅 agent stream 的 Chat/Tool 事件
//  - 投影为 AG-UI 事件
//  - 通过 Channel 推送给 SSE
// ============================================================

public sealed class SessionAgUiStream : IAsyncDisposable
{
    private const string MessageMetaEventName = "aevatar.vibe.message_meta";
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly BroadcastEventHub<AgUiEvent> _hub;
    private readonly IAgentMessageStreamResolver _streamResolver;
    private readonly ConcurrentDictionary<string, bool> _subscribedAgents = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<Task<IMessageStreamSubscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, int> _textDeltaCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _textLengths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _textFromStream = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _traceFirstSeenAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _textStartLogged = new(StringComparer.Ordinal);
    private int _traceSeenCount;
    private int _traceAssistantProbeCount;
    private int _traceEventLogCount;
    private int _disposed;

    public SessionAgUiStream(
        string sessionId,
        string agentId,
        IAgentMessageStreamResolver streamResolver,
        ILogger logger)
    {
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _streamResolver = streamResolver ?? throw new ArgumentNullException(nameof(streamResolver));

        _hub = new BroadcastEventHub<AgUiEvent>(
            replayBufferSize: 0,
            subscriberBufferSize: 512,
            warningLogger: msg => _logger.LogWarning("{Message}", msg),
            hubName: $"SessionAgUiStream:{sessionId}");

        AttachAgent(agentId);
    }

    public async IAsyncEnumerable<AgUiEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("[SessionAgUiStream] SSE subscriber connected: {SessionId}", _sessionId);
        try
        {
            await foreach (var evt in _hub.SubscribeAsync(replay: false, ct: ct))
                yield return evt;
        }
        finally
        {
            _logger.LogDebug("[SessionAgUiStream] SSE subscriber disconnected: {SessionId}", _sessionId);
        }
    }

    public void Publish(AgUiEvent evt)
    {
        if (evt == null || Volatile.Read(ref _disposed) != 0)
            return;
        if (evt is CustomEvent custom &&
            string.Equals(custom.Name, "aevatar.vibe.dag_updated", StringComparison.Ordinal))
        {
            // #region agent log
            File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = _sessionId,
                    runId = string.Empty,
                    hypothesisId = "H64",
                    location = "SessionAgUiStream.cs:Publish",
                    message = "custom_event_publish",
                    data = new
                    {
                        name = custom.Name ?? string.Empty,
                        streamHash = RuntimeHelpers.GetHashCode(this),
                        hasValue = custom.Value != null
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }
        if (evt is TextMessageStartEvent start)
        {
            // #region agent log
            File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = _sessionId,
                    runId = ExtractRunId(start.MessageId),
                    hypothesisId = "H9",
                    location = "SessionAgUiStream.cs:Publish",
                    message = "text_message_start",
                    data = new
                    {
                        messageId = start.MessageId ?? string.Empty,
                        role = start.Role ?? string.Empty
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion

            var mid = (start.MessageId ?? string.Empty).Trim();
            if (_textStartLogged.TryAdd(mid, true))
            {
                var parts = mid.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var role = parts.Length >= 4 ? parts[2] : string.Empty;
                var runId = ExtractRunId(mid);
                var key = $"{runId}:{role}";
                if (!string.IsNullOrWhiteSpace(runId) &&
                    !string.IsNullOrWhiteSpace(role) &&
                    _traceFirstSeenAt.TryGetValue(key, out var firstSeenAt))
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var elapsedMs = Math.Max(0, now - firstSeenAt);
                    // #region agent log
                    File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        JsonSerializer.Serialize(new
                        {
                            sessionId = _sessionId,
                            runId,
                            hypothesisId = "H32",
                            location = "SessionAgUiStream.cs:Publish",
                            message = "text_start_latency",
                            data = new
                            {
                                messageId = mid,
                                role,
                                elapsedMs
                            },
                            timestamp = now
                        }) + Environment.NewLine);
                    // #endregion
                }
            }
        }
        else if (evt is TextMessageContentEvent content)
        {
            var count = _textDeltaCounts.AddOrUpdate(content.MessageId ?? string.Empty, 1, (_, c) => c + 1);
            if (count <= 3)
            {
                // #region agent log
                File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId = ExtractRunId(content.MessageId),
                        hypothesisId = "H9",
                        location = "SessionAgUiStream.cs:Publish",
                        message = "text_message_content",
                        data = new
                        {
                            messageId = content.MessageId ?? string.Empty,
                            deltaLength = content.Delta?.Length ?? 0,
                            deltaIndex = count
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }
        }
        else if (evt is TextMessageEndEvent end)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = _sessionId,
                    runId = ExtractRunId(end.MessageId),
                    hypothesisId = "H9",
                    location = "SessionAgUiStream.cs:Publish",
                    message = "text_message_end",
                    data = new { messageId = end.MessageId ?? string.Empty },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }
        _hub.Publish(evt);
    }

    public void AttachAgent(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId) || Volatile.Read(ref _disposed) != 0)
            return;

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId = _sessionId,
                runId = string.Empty,
                hypothesisId = "H37",
                location = "SessionAgUiStream.cs:AttachAgent",
                message = "attach_agent",
                data = new { agentId },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        if (!_subscribedAgents.TryAdd(agentId, true))
            return;

        var stream = _streamResolver.GetStream(agentId);
        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId = _sessionId,
                runId = string.Empty,
                hypothesisId = "H6",
                location = "SessionAgUiStream.cs:AttachAgent",
                message = "stream_resolved",
                data = new
                {
                    agentId,
                    streamId = stream?.StreamId ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion
        SubscribeToAgentEvents(stream, agentId);
    }

    private void SubscribeToAgentEvents(IMessageStream stream, string agentId)
    {
        _logger.LogDebug("[SessionAgUiStream] Subscribing to agent {AgentId}", agentId);

        // Tool 事件
        Track(stream.SubscribeAsync<CoreToolCallStartEvent>(evt =>
        {
            Publish(new AgUiToolCallStartEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = evt.MessageId ?? string.Empty,
                ToolCallId = evt.ToolCallId ?? string.Empty,
                ToolName = evt.ToolName ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        Track(stream.SubscribeAsync<CoreToolCallEndEvent>(evt =>
        {
            Publish(new AgUiToolCallEndEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = evt.MessageId ?? string.Empty,
                ToolCallId = evt.ToolCallId ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        // Text message streaming from core agent events
        Track(stream.SubscribeAsync<CoreTextMessageStartEvent>(evt =>
        {
            var mid = (evt.MessageId ?? string.Empty).Trim();
            if (mid.Length == 0)
                return Task.CompletedTask;
            _textFromStream.TryAdd(mid, true);
            var agentKey = ExtractAgentFromMessageId(mid);
            if (string.IsNullOrWhiteSpace(agentKey))
                agentKey = "assistant";
            Publish(new CustomEvent
            {
                Name = MessageMetaEventName,
                Value = new
                {
                    messageId = mid,
                    agent = agentKey,
                    stepName = agentKey,
                    providerName = string.Empty
                }
            });
            Publish(new TextMessageStartEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = mid,
                Role = evt.Role ?? "assistant"
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        Track(stream.SubscribeAsync<CoreTextMessageContentEvent>(evt =>
        {
            Publish(new TextMessageContentEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = evt.MessageId ?? string.Empty,
                Delta = evt.Delta ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        Track(stream.SubscribeAsync<CoreTextMessageEndEvent>(evt =>
        {
            var mid = (evt.MessageId ?? string.Empty).Trim();
            if (mid.Length > 0)
                _textFromStream.TryRemove(mid, out _);
            Publish(new TextMessageEndEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = mid
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        // ExecutionTraceEvent - 过滤掉内部 chat handler 事件
        Track(stream.SubscribeAsync<ExecutionTraceEvent>(evt =>
        {
            // 跳过内部 chat 相关的 handler 事件，这些不应暴露给 UI
            if (evt.Fields.TryGetValue(ExecutionTraceEventFields.HandlerName, out var handlerField))
            {
                var handlerName = handlerField.StringValue ?? string.Empty;
                if (handlerName.StartsWith("HandleChat", StringComparison.Ordinal))
                    return Task.CompletedTask;
            }

            var status = ReadStringField(evt, ExecutionTraceEventFields.Status) ?? string.Empty;
            var stepType = ReadStringField(evt, ExecutionTraceEventFields.StepType) ?? string.Empty;
            var runId = ReadStringField(evt, ExecutionTraceEventFields.ExecutionId) ?? string.Empty;
            var nodeId = (evt.NodeId ?? string.Empty).Trim();
            var messageId = ReadStringField(evt, ExecutionTraceEventFields.MessageId);
            var assistantResponse = ReadStringField(evt, ExecutionTraceEventFields.AssistantResponse);
            var assistantLen = assistantResponse?.Length ?? 0;
            var hasAssistant = !string.IsNullOrWhiteSpace(assistantResponse);
            var hasMessageId = !string.IsNullOrWhiteSpace(messageId);
            var isTerminal =
                string.Equals(status, ExecutionTraceEventStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, ExecutionTraceEventStatus.Failed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, ExecutionTraceEventStatus.Cancelled, StringComparison.OrdinalIgnoreCase);

            if (Interlocked.Increment(ref _traceSeenCount) <= 3)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H28",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_seen",
                        data = new
                        {
                            agentId,
                            nodeId,
                            status,
                            stepType,
                            assistantLen,
                            hasAssistant,
                            hasMessageId
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }

            if (Interlocked.Increment(ref _traceEventLogCount) <= 3)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H7",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_received",
                        data = new
                        {
                            agentId,
                            nodeId,
                            status,
                            stepType,
                            assistantLen,
                            hasAssistant,
                            hasMessageId
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }

            if (assistantLen > 0 && Interlocked.Increment(ref _traceAssistantProbeCount) <= 3)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H55",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_assistant_in_agui",
                        data = new
                        {
                            nodeId,
                            status,
                            stepType,
                            assistantLen,
                            hasMessageId
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }

            if (!string.IsNullOrWhiteSpace(runId) && !string.IsNullOrWhiteSpace(nodeId))
            {
                var key = $"{runId}:{nodeId}";
                _traceFirstSeenAt.TryAdd(key, ToUnixMs(evt.Timestamp));
            }

            if (isTerminal)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H3",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_terminal_received",
                        data = new
                        {
                            agentId,
                            nodeId,
                            status,
                            stepType,
                            assistantLen,
                            hasAssistant,
                            hasMessageId
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion

                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H29",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_terminal",
                        data = new
                        {
                            nodeId,
                            status,
                            stepType,
                            assistantLen,
                            hasAssistant,
                            hasMessageId
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }
            if (!string.IsNullOrWhiteSpace(assistantResponse))
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H11",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_assistant_response",
                        data = new
                        {
                            agentId,
                            nodeId,
                            length = assistantLen,
                            status,
                            stepType
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }

            var agui = AgUiTraceProjector.Map(evt, new AgUiTraceProjectorOptions
            {
                ResolveThreadId = _ => _sessionId
            });
            var hasTextMessage = false;
            var aguiCount = 0;
            foreach (var e in agui)
            {
                aguiCount++;
                if (e is TextMessageStartEvent or TextMessageContentEvent)
                    hasTextMessage = true;
                Publish(e);
            }
            if (assistantLen > 0)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H73",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "trace_to_agui",
                        data = new
                        {
                            nodeId,
                            assistantLen,
                            aguiCount,
                            hasTextMessage
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }
            if (isTerminal)
            {
                // #region agent log
                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                    JsonSerializer.Serialize(new
                    {
                        sessionId = _sessionId,
                        runId,
                        hypothesisId = "H30",
                        location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                        message = "agui_mapped",
                        data = new
                        {
                            nodeId,
                            status,
                            stepType,
                            aguiCount,
                            hasTextMessage
                        },
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }) + Environment.NewLine);
                // #endregion
            }

            if (!hasTextMessage)
            {
                var response = ReadStringField(evt, ExecutionTraceEventFields.AssistantResponse);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var rawMessageId = ReadStringField(evt, ExecutionTraceEventFields.MessageId);
                    if (!string.IsNullOrWhiteSpace(rawMessageId) && _textFromStream.ContainsKey(rawMessageId))
                        return Task.CompletedTask;
                    var logRunId = ReadStringField(evt, ExecutionTraceEventFields.ExecutionId) ?? string.Empty;
                    // #region agent log
                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            sessionId = _sessionId,
                            runId = logRunId,
                            hypothesisId = "H4",
                            location = "SessionAgUiStream.cs:SubscribeToAgentEvents",
                            message = "fallback_text_emitted",
                            data = new
                            {
                                assistantResponseLength = response.Length,
                                hasMessageId = !string.IsNullOrWhiteSpace(ReadStringField(evt, ExecutionTraceEventFields.MessageId))
                            },
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        }) + Environment.NewLine);
                    // #endregion

                    if (response.Length > 120_000)
                        response = response[..120_000];

                    if (string.IsNullOrWhiteSpace(runId))
                        runId = "run";

                    var agentKey = nodeId.Length > 0 ? nodeId : "assistant";

                    var msgId = ResolveMessageId(
                        ReadStringField(evt, ExecutionTraceEventFields.MessageId),
                        _sessionId,
                        agentKey,
                        runId);
                    var ts = ToUnixMs(evt.Timestamp);

                    var lastLength = _textLengths.GetOrAdd(msgId, 0);
                    if (response.Length > lastLength)
                    {
                        var delta = response[lastLength..];
                        _textLengths[msgId] = response.Length;

                        if (lastLength == 0)
                        {
                            var providerName = ReadStringField(evt, ExecutionTraceEventFields.LlmModel) ?? string.Empty;
                            var stepName = nodeId.Length > 0 ? nodeId : agentKey;
                            Publish(new CustomEvent
                            {
                                Name = MessageMetaEventName,
                                Value = new
                                {
                                    messageId = msgId,
                                    agent = agentKey,
                                    stepName,
                                    providerName
                                }
                            });
                            Publish(new TextMessageStartEvent
                            {
                                Timestamp = ts,
                                MessageId = msgId,
                                Role = "assistant"
                            });
                        }

                        Publish(new TextMessageContentEvent
                        {
                            Timestamp = ts,
                            MessageId = msgId,
                            Delta = delta
                        });
                    }

                    if (isTerminal)
                    {
                        Publish(new TextMessageEndEvent
                        {
                            Timestamp = ts,
                            MessageId = msgId
                        });
                        _textLengths.TryRemove(msgId, out _);
                    }
                }
            }
            return Task.CompletedTask;
        }, null, CancellationToken.None));
    }

    private static long Now()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long ToUnixMs(Timestamp? ts)
        => ts == null ? Now() : new DateTimeOffset(ts.ToDateTime().ToUniversalTime()).ToUnixTimeMilliseconds();

    private static string? ReadStringField(ExecutionTraceEvent evt, string key)
    {
        if (evt.Fields == null) return null;
        if (!evt.Fields.TryGetValue(key, out var value)) return null;
        return value.StringValue;
    }

    private static string ResolveMessageId(string? messageId, string threadId, string role, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId;
        return $"msg:{threadId}:{role}:{suffix}";
    }

    private static string ExtractAgentFromMessageId(string? messageId)
    {
        var mid = (messageId ?? string.Empty).Trim();
        if (mid.Length == 0) return string.Empty;
        var parts = mid.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 4 ? parts[2] : string.Empty;
    }

    private static string ExtractRunId(string? messageId)
    {
        var mid = (messageId ?? string.Empty).Trim();
        if (mid.Length == 0) return string.Empty;
        var parts = mid.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 4 ? parts[^1] : string.Empty;
    }

    private void Track(Task<IMessageStreamSubscription> task)
        => _subscriptions.Add(task);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _hub.Complete();

        foreach (var task in _subscriptions)
        {
            try
            {
                var sub = await task.ConfigureAwait(false);
                await sub.UnsubscribeAsync().ConfigureAwait(false);
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
