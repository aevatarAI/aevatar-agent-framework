using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Maker;

// ============================================================
//  MAKER -> ExecutionTrace Adapter
//
//  WHY:
//  - MAKER has a rich domain trace (`MakerTrace`) but it's a POCO record.
//  - Cross-runtime/exportable trace must converge to unified Protobuf contract:
//    `Aevatar.Agents.Abstractions.Tracing.ExecutionTrace`.
//
//  NOTE:
//  - This adapter intentionally keeps mapping generic:
//    task tree -> nodes, voting -> decisions, red flags -> alerts.
// ============================================================
public static class MakerExecutionTraceExtensions
{
    public static ExecutionTrace ToExecutionTrace(this MakerResult result)
    {
        var now = DateTime.UtcNow;
        var duration = result.Trace.Duration;
        var startedAt = now - duration;

        return new ExecutionTrace
        {
            ExecutionId = result.Trace.ExecutionId,
            Kind = ExecutionTraceKind.Maker,
            Status = MapStatus(result.Success, result.Error),
            Name = "MAKER",
            Description = result.Trace.RootTask.Description,
            StartedAt = Timestamp.FromDateTime(startedAt),
            EndedAt = Timestamp.FromDateTime(now),
            Cost = new ExecutionTraceCost
            {
                DurationMs = (long)duration.TotalMilliseconds,
                TotalLlmCalls = result.Trace.TotalLLMCalls,
                TotalTokens = result.Trace.TotalTokens,
                PromptTokens = result.Trace.PromptTokens,
                CompletionTokens = result.Trace.CompletionTokens
            },
            Root = BuildTaskNode(result.Trace.RootTask, result.Trace.RedFlags),
            Error = result.Error ?? string.Empty
        };
    }

    private static ExecutionTraceStatus MapStatus(bool success, string? error)
    {
        if (success)
            return ExecutionTraceStatus.Succeeded;

        if (!string.IsNullOrEmpty(error) && error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return ExecutionTraceStatus.Timeout;

        return ExecutionTraceStatus.Failed;
    }

    private static ExecutionTraceNode BuildTaskNode(TaskNode task, IReadOnlyList<RedFlagEvent> rootRedFlags)
    {
        var node = new ExecutionTraceNode
        {
            NodeId = task.TaskId,
            Name = task.TaskId,
            Type = task.IsAtomic ? "atomic_task" : "task",
            Description = task.Description,
            Status = string.IsNullOrEmpty(task.Result)
                ? ExecutionTraceStatus.Unspecified
                : ExecutionTraceStatus.Succeeded,
            Output = task.Result ?? string.Empty
        };

        node.Metrics["depth"] = Cv(task.Depth);
        node.Metrics["is_atomic"] = Cv(task.IsAtomic);

        if (!string.IsNullOrEmpty(task.FallbackCandidate))
        {
            node.Metrics["fallback_candidate_preview"] = Cv(Preview(task.FallbackCandidate, 120));
        }

        // Voting sessions -> generic decisions
        for (var i = 0; i < task.VotingSessions.Count; i++)
        {
            node.Decisions.Add(ConvertDecision(task.TaskId, i, task.VotingSessions[i]));
        }

        // Root-level red flags -> alerts (keep it simple & discoverable)
        if (task.Depth == 0 && rootRedFlags.Count > 0)
        {
            for (var i = 0; i < rootRedFlags.Count; i++)
            {
                node.Alerts.Add(ConvertAlert(task.TaskId, i, rootRedFlags[i]));
            }
        }

        // Children
        foreach (var child in task.Children)
        {
            node.Children.Add(BuildTaskNode(child, rootRedFlags));
        }

        return node;
    }

    private static ExecutionTraceDecisionSession ConvertDecision(string taskId, int index, VotingSession session)
    {
        var decision = new ExecutionTraceDecisionSession
        {
            DecisionId = $"{taskId}:vote:{index}",
            Type = session.Type.ToString(),
            Rounds = session.Rounds,
            WinnerCandidateId = session.Winner?.Hash ?? string.Empty
        };

        foreach (var c in session.Candidates)
        {
            decision.Candidates.Add(new ExecutionTraceCandidate
            {
                CandidateId = c.Hash,
                Content = c.Content,
                Votes = c.Votes,
                Metrics =
                {
                    { "cluster_size", Cv(c.ClusterSize) }
                }
            });
        }

        return decision;
    }

    private static ExecutionTraceAlert ConvertAlert(string taskId, int index, RedFlagEvent redFlag)
    {
        return new ExecutionTraceAlert
        {
            AlertId = $"{taskId}:red_flag:{index}",
            Type = "red_flag",
            Message = redFlag.Reason,
            Recovered = redFlag.Recovered,
            Timestamp = Timestamp.FromDateTime(redFlag.Timestamp.UtcDateTime)
        };
    }

    private static Aevatar.Agents.ContextValue Cv(object value)
    {
        var cv = new Aevatar.Agents.ContextValue();

        switch (value)
        {
            case int i:
                cv.IntValue = i;
                return cv;
            case long l:
                cv.IntValue = l;
                return cv;
            case bool b:
                cv.BoolValue = b;
                return cv;
            case double d:
                cv.DoubleValue = d;
                return cv;
            default:
                cv.StringValue = value.ToString() ?? string.Empty;
                return cv;
        }
    }

    private static string Preview(string content, int maxChars)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
        return content.Length <= maxChars ? content : content[..maxChars] + "...";
    }
}


