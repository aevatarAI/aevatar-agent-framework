using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AGUI;

/// <summary>
/// Project ExecutionTraceEvent into AG-UI events (trace-only pipeline).
/// </summary>
public static class AgUiTraceProjector
{
    public const string LlmTraceEventName = "aevatar.llm.trace";

    public static IReadOnlyList<AgUiEvent> Map(
        ExecutionTraceEvent evt,
        AgUiTraceProjectorOptions? options = null)
    {
        if (evt == null) return Array.Empty<AgUiEvent>();

        var opts = options ?? new AgUiTraceProjectorOptions();
        var phase = ReadStringField(evt, ExecutionTraceEventFields.Phase) ?? evt.Phase ?? string.Empty;
        var status = ReadStringField(evt, ExecutionTraceEventFields.Status);
        var sessionId = ReadStringField(evt, ExecutionTraceEventFields.SessionId) ?? string.Empty;
        var executionId = ReadStringField(evt, ExecutionTraceEventFields.ExecutionId);

        var threadId = opts.ResolveThreadId?.Invoke(evt) ?? (sessionId.Length > 0 ? sessionId : executionId ?? "thread");
        var runId = opts.ResolveRunId?.Invoke(evt) ?? (executionId ?? sessionId ?? "run");
        var messageId = opts.ResolveMessageId?.Invoke(evt) ??
                        ReadStringField(evt, ExecutionTraceEventFields.MessageId);
        var toolCallId = opts.ResolveToolCallId?.Invoke(evt) ??
                         ReadStringField(evt, ExecutionTraceEventFields.ToolCallId);
        var toolName = opts.ResolveToolName?.Invoke(evt) ??
                       ReadStringField(evt, ExecutionTraceEventFields.ToolName);
        var role = opts.ResolveRole?.Invoke(evt) ?? "assistant";

        var timestamp = ToUnixMilliseconds(evt.Timestamp);
        var events = new List<AgUiEvent>(capacity: 3);

        if (IsPhase(phase, ExecutionTraceEventPhase.SessionStart))
        {
            events.Add(new RunStartedEvent
            {
                Timestamp = timestamp,
                ThreadId = threadId,
                RunId = runId,
                RawEvent = evt
            });
            events.Add(new CustomEvent
            {
                Timestamp = timestamp,
                Name = "session.start",
                Value = new
                {
                    sessionId,
                    executionId,
                    status = status ?? ExecutionTraceEventStatus.Running
                },
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.SessionStop))
        {
            if (IsFailureStatus(status))
            {
                events.Add(new RunErrorEvent
                {
                    Timestamp = timestamp,
                    Message = ReadStringField(evt, ExecutionTraceEventFields.Error) ?? "run failed",
                    Code = status,
                    RawEvent = evt
                });
            }
            else
            {
                events.Add(new RunFinishedEvent
                {
                    Timestamp = timestamp,
                    ThreadId = threadId,
                    RunId = runId,
                    Result = new
                    {
                        status = status ?? ExecutionTraceEventStatus.Completed,
                        duration_ms = ReadLongField(evt, ExecutionTraceEventFields.DurationMs)
                    },
                    RawEvent = evt
                });
            }
            events.Add(new CustomEvent
            {
                Timestamp = timestamp,
                Name = "session.stop",
                Value = new
                {
                    sessionId,
                    executionId,
                    status = status ?? ExecutionTraceEventStatus.Completed,
                    durationMs = ReadLongField(evt, ExecutionTraceEventFields.DurationMs)
                },
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.ToolStart))
        {
            events.Add(new ToolCallStartEvent
            {
                Timestamp = timestamp,
                MessageId = ResolveMessageId(messageId, threadId, "tool", toolCallId ?? toolName ?? "call"),
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? "tool",
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.ToolProgress))
        {
            events.Add(new ToolCallResultEvent
            {
                Timestamp = timestamp,
                MessageId = ResolveMessageId(messageId, threadId, "tool", toolCallId ?? toolName ?? "call"),
                ToolCallId = toolCallId ?? string.Empty,
                Result = evt.Message ?? string.Empty,
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.ToolEnd))
        {
            var msgId = ResolveMessageId(messageId, threadId, "tool", toolCallId ?? toolName ?? "call");
            var result = ReadStringField(evt, ExecutionTraceEventFields.Error) ?? evt.Message ?? status ?? "completed";

            events.Add(new ToolCallResultEvent
            {
                Timestamp = timestamp,
                MessageId = msgId,
                ToolCallId = toolCallId ?? string.Empty,
                Result = result,
                RawEvent = evt
            });
            events.Add(new ToolCallEndEvent
            {
                Timestamp = timestamp,
                MessageId = msgId,
                ToolCallId = toolCallId ?? string.Empty,
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.LlmResponse) ||
            IsPhase(phase, ExecutionTraceEventPhase.LlmRequest))
        {
            var content = ReadStringField(evt, ExecutionTraceEventFields.AssistantResponse);
            if (!string.IsNullOrWhiteSpace(content))
            {
                var msgId = ResolveMessageId(messageId, threadId, role, runId);
                events.Add(new TextMessageStartEvent
                {
                    Timestamp = timestamp,
                    MessageId = msgId,
                    Role = role,
                    RawEvent = evt
                });
                events.Add(new TextMessageContentEvent
                {
                    Timestamp = timestamp,
                    MessageId = msgId,
                    Delta = content!,
                    RawEvent = evt
                });
                events.Add(new TextMessageEndEvent
                {
                    Timestamp = timestamp,
                    MessageId = msgId,
                    RawEvent = evt
                });
                return events;
            }

            events.Add(new CustomEvent
            {
                Timestamp = timestamp,
                Name = LlmTraceEventName,
                Value = new
                {
                    phase,
                    status,
                    message = evt.Message ?? string.Empty,
                    sessionId,
                    executionId
                },
                RawEvent = evt
            });
            return events;
        }

        if (IsPhase(phase, ExecutionTraceEventPhase.EventHandlerStart) ||
            IsPhase(phase, ExecutionTraceEventPhase.EventHandlerEnd))
        {
            events.Add(new CustomEvent
            {
                Timestamp = timestamp,
                Name = phase,
                Value = new
                {
                    phase,
                    status,
                    sessionId,
                    executionId,
                    eventType = ReadStringField(evt, ExecutionTraceEventFields.EventType),
                    handlerName = ReadStringField(evt, ExecutionTraceEventFields.HandlerName),
                    handlerType = ReadStringField(evt, ExecutionTraceEventFields.HandlerType),
                    durationMs = ReadLongField(evt, ExecutionTraceEventFields.DurationMs),
                    error = ReadStringField(evt, ExecutionTraceEventFields.Error)
                },
                RawEvent = evt
            });
            return events;
        }

        // Fallback to existing execution trace mapper for workflow steps.
        return AgUiExecutionTraceMapper.Map(evt);
    }

    private static string? ReadStringField(ExecutionTraceEvent evt, string key)
    {
        if (evt.Fields == null) return null;
        if (!evt.Fields.TryGetValue(key, out var value)) return null;
        return value.ValueCase switch
        {
            ContextValue.ValueOneofCase.StringValue => value.StringValue,
            ContextValue.ValueOneofCase.GuidString => value.GuidString,
            ContextValue.ValueOneofCase.DatetimeIso => value.DatetimeIso,
            _ => null
        };
    }

    private static long? ReadLongField(ExecutionTraceEvent evt, string key)
    {
        if (evt.Fields == null) return null;
        if (!evt.Fields.TryGetValue(key, out var value)) return null;
        return value.ValueCase switch
        {
            ContextValue.ValueOneofCase.IntValue => value.IntValue,
            ContextValue.ValueOneofCase.DoubleValue => (long)value.DoubleValue,
            _ => null
        };
    }

    private static bool IsPhase(string? phase, string expected)
        => string.Equals(phase, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureStatus(string? status)
    {
        return string.Equals(status, ExecutionTraceEventStatus.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, ExecutionTraceEventStatus.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMessageId(string? messageId, string threadId, string role, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
            return messageId;
        return $"msg:{threadId}:{role}:{suffix}";
    }

    private static long? ToUnixMilliseconds(Timestamp? timestamp)
    {
        if (timestamp == null)
            return null;

        var dt = timestamp.ToDateTime();
        if (dt.Kind != DateTimeKind.Utc)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
    }
}
