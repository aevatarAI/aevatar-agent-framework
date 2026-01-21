using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AGUI;

// ============================================================
//  AG-UI Mapper: ExecutionTraceEvent -> Step + Custom
//
//  Unified fields contract (ExecutionTraceEvent.fields):
//  - status: pending/running/completed/failed/cancelled
//  - progress: 0..1
//  - execution_id / workflow_name / step_type / depth
//  - vote_round / vote_max_rounds / vote_k / vote_current_votes
//  - winner_proposal_id / winner_hash / winner_votes / winner_runner_up_votes
//    winner_cluster_count / winner_semantic / winner_is_consensus
//  - parallel_total / parallel_completed / parallel_failed
//  - worker_id / proposal_id / tokens_used / llm_calls / prompt_tokens / completion_tokens
// ============================================================
public static class AgUiExecutionTraceMapper
{
    public const string WorkflowExecutionEventName = "aevatar.workflow.execution_event";

    public static IReadOnlyList<AgUiEvent> Map(ExecutionTraceEvent evt)
    {
        if (evt == null) return Array.Empty<AgUiEvent>();

        var events = new List<AgUiEvent>(capacity: 2);
        var status = ReadStatus(evt);
        var timestamp = ToUnixMilliseconds(evt.Timestamp);
        var stepName = ResolveStepName(evt);

        if (IsStartStatus(status))
        {
            events.Add(new StepStartedEvent
            {
                Timestamp = timestamp,
                StepName = stepName,
                RawEvent = evt
            });
        }

        if (IsFinishStatus(status))
        {
            events.Add(new StepFinishedEvent
            {
                Timestamp = timestamp,
                StepName = stepName,
                RawEvent = evt
            });
        }

        events.Add(new CustomEvent
        {
            Timestamp = timestamp,
            Name = WorkflowExecutionEventName,
            Value = BuildPayload(evt, status),
            RawEvent = evt
        });

        return events;
    }

    private static object BuildPayload(ExecutionTraceEvent evt, string? status)
    {
        var fields = ToDictionary(evt.Fields);
        return new
        {
            phase = evt.Phase ?? string.Empty,
            nodeId = evt.NodeId ?? string.Empty,
            message = evt.Message ?? string.Empty,
            status = status ?? string.Empty,
            timestamp = ToUnixMilliseconds(evt.Timestamp),
            fields
        };
    }

    private static string ResolveStepName(ExecutionTraceEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.NodeId))
            return evt.NodeId;
        if (!string.IsNullOrWhiteSpace(evt.Phase))
            return evt.Phase;
        return "step";
    }

    private static bool IsStartStatus(string? status)
    {
        return string.Equals(status, ExecutionTraceEventStatus.Running, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, ExecutionTraceEventStatus.Pending, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFinishStatus(string? status)
    {
        return string.Equals(status, ExecutionTraceEventStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, ExecutionTraceEventStatus.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, ExecutionTraceEventStatus.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadStatus(ExecutionTraceEvent evt)
    {
        if (evt.Fields == null)
            return null;

        if (evt.Fields.TryGetValue(ExecutionTraceEventFields.Status, out var value))
        {
            if (value.ValueCase == ContextValue.ValueOneofCase.StringValue)
                return value.StringValue;

            var normalized = ToJsonValue(value);
            return normalized?.ToString();
        }

        return null;
    }

    private static Dictionary<string, object?> ToDictionary(MapField<string, ContextValue> fields)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (fields == null || fields.Count == 0) return result;

        foreach (var kv in fields)
        {
            result[kv.Key] = ToJsonValue(kv.Value);
        }

        return result;
    }

    private static object? ToJsonValue(ContextValue value)
    {
        return value.ValueCase switch
        {
            ContextValue.ValueOneofCase.StringValue => value.StringValue,
            ContextValue.ValueOneofCase.BoolValue => value.BoolValue,
            ContextValue.ValueOneofCase.IntValue => value.IntValue,
            ContextValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            ContextValue.ValueOneofCase.DatetimeIso => value.DatetimeIso,
            ContextValue.ValueOneofCase.GuidString => value.GuidString,
            _ => null
        };
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
