using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  Agentic RAG - ExecutionTrace export helpers (best-effort)
//
//  中文 + ASCII:
//  - ExecutionTrace 是可导出的统一 trace 契约（Protobuf）。
//  - 这里构建的 trace 必须有界：不要 dump 大段原文/答案/证据。
//  - Export 失败不得影响主链路（best-effort）。
// ============================================================

internal sealed class AgenticRagIterationTrace
{
    public int Iteration { get; init; }
    public bool PlanShouldStop { get; set; }
    public int RetrievalCount { get; set; }
    public int EvidenceCount { get; set; }
    public int DraftChars { get; set; }
    public bool CriticPassed { get; set; }
    public int CriticGaps { get; set; }
    public string? ErrorStage { get; set; }
    public string? ErrorMessage { get; set; }
}

internal static class AgenticRagExecutionTraceExporter
{
    public static async Task<bool> TryExportAsync(
        ILogger logger,
        IExecutionTraceStore store,
        string requestId,
        string runId,
        string agentId,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        long durationMs,
        RagStopReason stopReason,
        int iterations,
        IReadOnlyList<RagEvidenceSummary> evidence,
        string answer,
        Dictionary<string, string> diagnostics,
        IReadOnlyList<AgenticRagIterationTrace> iterationTraces)
    {
        try
        {
            var trace = BuildExecutionTrace(
                requestId,
                runId,
                agentId,
                startedAtUtc,
                endedAtUtc,
                durationMs,
                stopReason,
                iterations,
                evidence,
                answer,
                iterationTraces);

            await store.SaveAsync(trace, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            diagnostics["execution_trace_error"] = ex.GetType().Name;
            diagnostics["execution_trace_error_message"] = ex.Message;
            logger.LogWarning(ex, "Failed to export ExecutionTrace (runId={RunId})", runId);
            return false;
        }
    }

    private static ExecutionTrace BuildExecutionTrace(
        string requestId,
        string runId,
        string agentId,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        long durationMs,
        RagStopReason stopReason,
        int iterations,
        IReadOnlyList<RagEvidenceSummary> evidence,
        string answer,
        IReadOnlyList<AgenticRagIterationTrace> iterationTraces)
    {
        var status = MapTraceStatus(stopReason);
        var started = Timestamp.FromDateTime(startedAtUtc);
        var ended = Timestamp.FromDateTime(endedAtUtc);

        var trace = new ExecutionTrace
        {
            ExecutionId = runId,
            Kind = ExecutionTraceKind.Custom,
            Status = status,
            Name = "agentic_rag",
            Description = "Agentic RAG bounded loop (plan/retrieve/synthesize/critique)",
            StartedAt = started,
            EndedAt = ended,
            Cost = new ExecutionTraceCost
            {
                DurationMs = durationMs,
                TotalLlmCalls = 0,
                TotalTokens = 0
            },
            Root = new ExecutionTraceNode
            {
                NodeId = runId,
                Name = "agentic_rag",
                Type = "agentic_rag",
                Status = status,
                StartedAt = started,
                EndedAt = ended,
                Cost = new ExecutionTraceCost { DurationMs = durationMs },
                Output = Truncate(answer, 512)
            },
            Error = stopReason == RagStopReason.Failed ? "failed" : string.Empty
        };

        trace.Labels["agentic_rag.request_id"] = requestId;
        trace.Labels["agentic_rag.agent_id"] = agentId ?? string.Empty;
        trace.Labels["agentic_rag.stop_reason"] = stopReason.ToString();
        trace.Labels["agentic_rag.iterations"] = Math.Max(0, iterations).ToString();
        trace.Labels["agentic_rag.evidence_items"] = evidence.Count.ToString();

        foreach (var it in iterationTraces)
        {
            var iterStatus = string.IsNullOrWhiteSpace(it.ErrorStage)
                ? ExecutionTraceStatus.Succeeded
                : ExecutionTraceStatus.Failed;

            var iterNodeId = $"{runId}/iter/{it.Iteration}";
            var iterNode = new ExecutionTraceNode
            {
                NodeId = iterNodeId,
                Name = $"iteration {it.Iteration}",
                Type = "iteration",
                Status = iterStatus,
                Cost = new ExecutionTraceCost { DurationMs = 0 },
                Output = $"stop_hint={it.PlanShouldStop}, retrievals={it.RetrievalCount}, evidence={it.EvidenceCount}, critic_passed={it.CriticPassed}, gaps={it.CriticGaps}",
                Error = string.IsNullOrWhiteSpace(it.ErrorStage)
                    ? string.Empty
                    : $"{it.ErrorStage}: {Truncate(it.ErrorMessage, 256)}"
            };

            iterNode.Children.Add(BuildStageNode(iterNodeId, "plan", it));
            iterNode.Children.Add(BuildStageNode(iterNodeId, "retrieve", it));
            iterNode.Children.Add(BuildStageNode(iterNodeId, "synthesize", it));
            iterNode.Children.Add(BuildStageNode(iterNodeId, "critique", it));

            trace.Root.Children.Add(iterNode);
        }

        return trace;
    }

    private static ExecutionTraceNode BuildStageNode(string iterNodeId, string stage, AgenticRagIterationTrace it)
    {
        var errorStage = it.ErrorStage ?? string.Empty;
        var status = StageStatus(stage, errorStage);

        var output = stage switch
        {
            "plan" => $"should_stop={it.PlanShouldStop}",
            "retrieve" => $"retrievals={it.RetrievalCount}, evidence={it.EvidenceCount}",
            "synthesize" => $"draft_chars={it.DraftChars}",
            "critique" => $"passed={it.CriticPassed}, gaps={it.CriticGaps}",
            _ => string.Empty
        };

        return new ExecutionTraceNode
        {
            NodeId = $"{iterNodeId}/{stage}",
            Name = stage,
            Type = stage,
            Status = status,
            Cost = new ExecutionTraceCost { DurationMs = 0 },
            Output = output,
            Error = string.Equals(errorStage, stage, StringComparison.OrdinalIgnoreCase)
                ? Truncate(it.ErrorMessage, 256)
                : string.Empty
        };
    }

    private static ExecutionTraceStatus StageStatus(string stage, string errorStage)
    {
        if (string.IsNullOrWhiteSpace(errorStage))
            return ExecutionTraceStatus.Succeeded;

        var s = StageRank(stage);
        var e = StageRank(errorStage);
        if (s < e) return ExecutionTraceStatus.Succeeded;
        if (s == e) return ExecutionTraceStatus.Failed;
        return ExecutionTraceStatus.Unspecified;
    }

    private static int StageRank(string stage)
    {
        return stage.ToLowerInvariant() switch
        {
            "plan" => 1,
            "retrieve" => 2,
            "synthesize" => 3,
            "critique" => 4,
            _ => 99
        };
    }

    private static ExecutionTraceStatus MapTraceStatus(RagStopReason stopReason)
    {
        return stopReason switch
        {
            RagStopReason.Succeeded => ExecutionTraceStatus.Succeeded,
            RagStopReason.Cancelled => ExecutionTraceStatus.Cancelled,
            RagStopReason.BudgetExceeded => ExecutionTraceStatus.Timeout,
            RagStopReason.NoEvidence => ExecutionTraceStatus.Failed,
            RagStopReason.Failed => ExecutionTraceStatus.Failed,
            _ => ExecutionTraceStatus.Unspecified
        };
    }

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0)
            return string.Empty;

        var s = text.Replace("\r", "").Trim();
        return s.Length <= maxChars ? s : s[..maxChars] + "...";
    }
}


