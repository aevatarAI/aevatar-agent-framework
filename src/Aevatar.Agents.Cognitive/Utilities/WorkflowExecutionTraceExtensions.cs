using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Utilities;

// ============================================================
//  Cognitive Workflow -> ExecutionTrace Adapter
//
//  Input:
//  - WorkflowStepEvent stream (already Protobuf, used for UI/replay)
//
//  Output:
//  - Unified Protobuf trace: `ExecutionTrace`
//
//  Goal:
//  - Make workflow replay data exportable/analyzable with the same schema
//    as MAKER / UoT.
// ============================================================
public static class WorkflowExecutionTraceExtensions
{
    public static ExecutionTrace ToExecutionTrace(this IEnumerable<WorkflowStepEvent> stepEvents)
    {
        var events = (stepEvents ?? Array.Empty<WorkflowStepEvent>())
            .Where(e => e != null)
            .OrderBy(e => e.Timestamp.ToDateTime())
            .ToList();

        if (events.Count == 0)
        {
            return new ExecutionTrace
            {
                Kind = ExecutionTraceKind.Workflow,
                Status = ExecutionTraceStatus.Unspecified,
                Name = "Workflow",
                Root = new ExecutionTraceNode
                {
                    NodeId = "empty",
                    Name = "Workflow",
                    Type = "workflow",
                    Status = ExecutionTraceStatus.Unspecified
                }
            };
        }

        var first = events[0];
        var startedAt = events[0].Timestamp;
        var endedAt = events[^1].Timestamp;

        var totalTokens = events.Max(e => e.TokensUsed);
        var totalCalls = events.Max(e => e.LlmCalls);
        var durationMs = (long)(endedAt.ToDateTime() - startedAt.ToDateTime()).TotalMilliseconds;

        var trace = new ExecutionTrace
        {
            ExecutionId = first.RunId,
            Kind = ExecutionTraceKind.Workflow,
            Status = DetermineTraceStatus(events),
            Name = first.WorkflowName,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Cost = new ExecutionTraceCost
            {
                DurationMs = durationMs,
                TotalTokens = totalTokens,
                TotalLlmCalls = totalCalls
            }
        };

        trace.Root = BuildWorkflowTree(first, events, trace.Status);
        trace.Events.AddRange(events.Select(ToTraceEvent));

        trace.Metrics["steps_total"] = Cv(events.Select(e => e.StepId).Distinct().Count());
        trace.Metrics["tokens_used"] = Cv(totalTokens);
        trace.Metrics["llm_calls"] = Cv(totalCalls);

        return trace;
    }

    private static ExecutionTraceStatus DetermineTraceStatus(IReadOnlyList<WorkflowStepEvent> events)
    {
        if (events.Any(e => e.Status == StepStatus.Failed))
            return ExecutionTraceStatus.Failed;

        if (events.Any(e => e.Status is StepStatus.Running or StepStatus.Pending))
            return ExecutionTraceStatus.Running;

        if (events.Any(e => e.Status == StepStatus.Completed))
            return ExecutionTraceStatus.Succeeded;

        return ExecutionTraceStatus.Unspecified;
    }

    private static ExecutionTraceNode BuildWorkflowTree(
        WorkflowStepEvent first,
        IReadOnlyList<WorkflowStepEvent> events,
        ExecutionTraceStatus overallStatus)
    {
        var nodes = BuildStepNodes(events);

        var root = new ExecutionTraceNode
        {
            NodeId = first.RunId,
            Name = first.WorkflowName,
            Type = "workflow",
            Status = overallStatus,
            Description = "Cognitive workflow execution"
        };

        foreach (var node in nodes.Values)
        {
            if (string.IsNullOrEmpty(node.ParentId) || !nodes.ContainsKey(node.ParentId))
            {
                root.Children.Add(node.Node);
            }
        }

        // Link children
        foreach (var node in nodes.Values)
        {
            if (!string.IsNullOrEmpty(node.ParentId) && nodes.TryGetValue(node.ParentId, out var parent))
            {
                parent.Node.Children.Add(node.Node);
            }
        }

        return root;
    }

    private static Dictionary<string, StepNodeBuilder> BuildStepNodes(IReadOnlyList<WorkflowStepEvent> events)
    {
        var dict = new Dictionary<string, StepNodeBuilder>(StringComparer.Ordinal);

        foreach (var e in events)
        {
            if (string.IsNullOrEmpty(e.StepId))
                continue;

            if (!dict.TryGetValue(e.StepId, out var b))
            {
                b = new StepNodeBuilder(e.StepId);
                dict[e.StepId] = b;
            }

            b.Apply(e);
        }

        return dict;
    }

    private static ExecutionTraceEvent ToTraceEvent(WorkflowStepEvent e)
    {
        return new ExecutionTraceEvent
        {
            Timestamp = e.Timestamp,
            Phase = e.StepType,
            Message = e.Message,
            NodeId = e.StepId,
            Fields =
            {
                { "status", Cv(e.Status.ToString()) },
                { "progress", Cv((double)e.Progress) },
                { "duration_ms", Cv(e.DurationMs) },
                { "tokens_used", Cv(e.TokensUsed) },
                { "llm_calls", Cv(e.LlmCalls) }
            }
        };
    }

    private sealed class StepNodeBuilder
    {
        public string ParentId { get; private set; } = string.Empty;
        public ExecutionTraceNode Node { get; }

        private Timestamp? _firstTs;
        private Timestamp? _lastTs;

        public StepNodeBuilder(string stepId)
        {
            Node = new ExecutionTraceNode
            {
                NodeId = stepId,
                Name = stepId,
                Type = "step",
                Status = ExecutionTraceStatus.Unspecified
            };
        }

        public void Apply(WorkflowStepEvent e)
        {
            ParentId = e.ParentStepId ?? string.Empty;

            Node.Type = string.IsNullOrEmpty(e.StepType) ? "step" : e.StepType;
            Node.Description = e.Message;

            _firstTs ??= e.Timestamp;
            _lastTs = e.Timestamp;

            Node.StartedAt ??= _firstTs;
            Node.EndedAt = _lastTs;
            Node.Status = MapStepStatus(e.Status);

            Node.Metrics["depth"] = Cv(e.Depth);
            Node.Metrics["progress"] = Cv((double)e.Progress);
            Node.Metrics["duration_ms"] = Cv(e.DurationMs);
            Node.Metrics["tokens_used"] = Cv(e.TokensUsed);
            Node.Metrics["llm_calls"] = Cv(e.LlmCalls);

            // Vote / fan_out metrics (present only for those step types).
            if (e.VoteMaxRounds > 0)
            {
                Node.Metrics["vote_round"] = Cv(e.VoteRound);
                Node.Metrics["vote_max_rounds"] = Cv(e.VoteMaxRounds);
                Node.Metrics["vote_k"] = Cv(e.VoteK);
                Node.Metrics["vote_current_votes"] = Cv(e.VoteCurrentVotes);
            }

            if (e.ParallelTotal > 0)
            {
                Node.Metrics["parallel_total"] = Cv(e.ParallelTotal);
                Node.Metrics["parallel_completed"] = Cv(e.ParallelCompleted);
                Node.Metrics["parallel_failed"] = Cv(e.ParallelFailed);
            }

            // Keep final assistant response (small) for completed steps.
            if (e.Status == StepStatus.Completed && !string.IsNullOrEmpty(e.AssistantResponse))
            {
                Node.Output = e.AssistantResponse;
            }

            // Red flag -> alert
            if (!string.IsNullOrEmpty(e.RedFlagReason))
            {
                Node.Alerts.Add(new ExecutionTraceAlert
                {
                    AlertId = $"{e.StepId}:red_flag:{e.Timestamp.Seconds}",
                    Type = "red_flag",
                    Message = e.RedFlagReason,
                    Recovered = false,
                    Timestamp = e.Timestamp
                });
            }
        }
    }

    private static ExecutionTraceStatus MapStepStatus(StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => ExecutionTraceStatus.Running,
            StepStatus.Running => ExecutionTraceStatus.Running,
            StepStatus.Completed => ExecutionTraceStatus.Succeeded,
            StepStatus.Failed => ExecutionTraceStatus.Failed,
            StepStatus.Skipped => ExecutionTraceStatus.Cancelled,
            _ => ExecutionTraceStatus.Unspecified
        };
    }

    private static Aevatar.Agents.ContextValue Cv(int value) => new() { IntValue = value };
    private static Aevatar.Agents.ContextValue Cv(long value) => new() { IntValue = value };
    private static Aevatar.Agents.ContextValue Cv(double value) => new() { DoubleValue = value };
    private static Aevatar.Agents.ContextValue Cv(string value) => new() { StringValue = value };
}


