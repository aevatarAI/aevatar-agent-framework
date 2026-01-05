using Aevatar.Agents;
using Aevatar.Agents.AI;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Notebook.Tracing;

// ============================================================
//  NotebookTraceBuilder
//
//  目的：
//  - 把 Notebook 的一次 Q&A 执行过程组织为 ExecutionTrace（树 + 决策）
//  - 让框架的 ProjectingExecutionTraceStore 自动投影：
//    ExecutionTrace -> MemoryGraph (Layer 4.2) + execution-scoped MemoryEntry (Layer 4)
//
//  约束：
//  - 内容必须有界：避免把完整 sources/context/raw prompt dump 到 trace
//  - 不包含 secrets：只记录可观测的元信息与截断预览
// ============================================================
internal static class NotebookTraceBuilder
{
    public static ExecutionTrace BuildChatTrace(NotebookChatTraceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var startedAt = Timestamp.FromDateTime(data.StartedAtUtc.ToUniversalTime());
        var endedAt = Timestamp.FromDateTime(data.EndedAtUtc.ToUniversalTime());

        var status = string.IsNullOrWhiteSpace(data.Error)
            ? ExecutionTraceStatus.Succeeded
            : ExecutionTraceStatus.Failed;

        var trace = new ExecutionTrace
        {
            ExecutionId = data.ExecutionId,
            Kind = ExecutionTraceKind.Custom,
            Status = status,
            Name = "notebook.chat",
            Description = Trunc($"query: {data.Query}"),
            StartedAt = startedAt,
            EndedAt = endedAt,
            Error = data.Error ?? string.Empty
        };

        trace.Labels["request_id"] = data.RequestId;
        trace.Labels["agent_id"] = data.AgentId;
        if (!string.IsNullOrWhiteSpace(data.LlmProvider))
            trace.Labels["llm_provider"] = data.LlmProvider!;

        trace.Metrics["sources_count"] = Cv(data.SourceIds?.Count ?? 0);

        // Root node (chat)
        var root = new ExecutionTraceNode
        {
            NodeId = "chat",
            Name = "chat",
            Type = "notebook.chat",
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = Trunc(data.Query),
            Output = Trunc(data.Response?.Content),
            Error = data.Error ?? string.Empty
        };

        // Context node (build context)
        var contextNode = BuildContextNode(data.Context, data.RenderedContext, data.Error, startedAt, endedAt);
        root.Children.Add(contextNode);

        // LLM node (generation)
        var llmNode = BuildLlmNode(data, startedAt, endedAt);
        root.Children.Add(llmNode);

        trace.Root = root;
        trace.Cost = BuildCost(data);

        return trace;
    }

    public static ExecutionTrace BuildReportTrace(NotebookReportTraceData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var startedAt = Timestamp.FromDateTime(data.StartedAtUtc.ToUniversalTime());
        var endedAt = Timestamp.FromDateTime(data.EndedAtUtc.ToUniversalTime());

        var status = string.IsNullOrWhiteSpace(data.Error)
            ? ExecutionTraceStatus.Succeeded
            : ExecutionTraceStatus.Failed;

        var trace = new ExecutionTrace
        {
            ExecutionId = data.ExecutionId,
            Kind = ExecutionTraceKind.Custom,
            Status = status,
            Name = "notebook.report",
            Description = Trunc($"topic: {data.Topic}"),
            StartedAt = startedAt,
            EndedAt = endedAt,
            Error = data.Error ?? string.Empty,
            Cost = new ExecutionTraceCost
            {
                DurationMs = (long)Math.Max(0, (data.EndedAtUtc - data.StartedAtUtc).TotalMilliseconds),
                TotalLlmCalls = 3,
                TotalTokens = 0,
                PromptTokens = 0,
                CompletionTokens = 0
            }
        };

        trace.Labels["agent_id"] = data.AgentId;
        trace.Labels["report_id"] = data.ReportId ?? string.Empty;
        trace.Labels["version"] = data.Version.ToString();
        if (!string.IsNullOrWhiteSpace(data.LlmProvider))
            trace.Labels["llm_provider"] = data.LlmProvider!;

        trace.Metrics["sources_count"] = Cv(data.SourceIds?.Count ?? 0);

        var root = new ExecutionTraceNode
        {
            NodeId = "report",
            Name = "report",
            Type = "notebook.report",
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = Trunc(data.Topic),
            Output = Trunc(data.Content, 800),
            Error = data.Error ?? string.Empty
        };

        // Context node
        root.Children.Add(BuildContextNode(data.Context, data.RenderedContext, data.Error, startedAt, endedAt));

        // Pipeline step nodes (outline -> draft -> refine)
        root.Children.Add(new ExecutionTraceNode
        {
            NodeId = "outline",
            Name = "outline",
            Type = "report.step",
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = "outline",
            Output = Trunc(data.Outline, 800)
        });

        root.Children.Add(new ExecutionTraceNode
        {
            NodeId = "draft",
            Name = "draft",
            Type = "report.step",
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = "draft",
            Output = Trunc(data.Draft, 800)
        });

        root.Children.Add(new ExecutionTraceNode
        {
            NodeId = "refine",
            Name = "refine",
            Type = "report.step",
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = "final",
            Output = Trunc(data.Content, 800)
        });

        trace.Root = root;
        return trace;
    }

    private static ExecutionTraceNode BuildContextNode(
        NotebookContext? context,
        string? renderedContext,
        string? error,
        Timestamp startedAt,
        Timestamp endedAt)
    {
        var ok = context != null && string.IsNullOrWhiteSpace(error);

        var node = new ExecutionTraceNode
        {
            NodeId = "context",
            Name = "build_context",
            Type = "notebook.context",
            Status = ok ? ExecutionTraceStatus.Succeeded : ExecutionTraceStatus.Failed,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = "Build notebook_context (coverage + top-k)",
            Output = string.Empty,
            Error = ok ? string.Empty : (error ?? "context unavailable")
        };

        if (context == null)
            return node;

        var strategy = context.Tags.TryGetValue("strategy", out var s) ? s : string.Empty;
        var chunkSliceCount = context.Slices.Count(x => x.Kind == NotebookContextSliceKind.SourceChunk);
        var previewCount = context.Slices.Count(x => x.Kind == NotebookContextSliceKind.SourcePreview);

        node.Labels["strategy"] = strategy;
        node.Metrics["preview_count"] = Cv(previewCount);
        node.Metrics["chunk_slice_count"] = Cv(chunkSliceCount);
        node.Metrics["rendered_chars"] = Cv((renderedContext ?? string.Empty).Length);

        // Bounded preview (for debugging only).
        node.Output = Trunc(renderedContext, maxChars: 800);

        // Encode top-k chunk selection as a "decision session" so graph has explicit candidates.
        var decision = new ExecutionTraceDecisionSession
        {
            DecisionId = "context:chunk_selection",
            Type = string.IsNullOrWhiteSpace(strategy) ? "topk" : $"topk:{strategy}",
            Rounds = 1
        };

        var candidates = context.Slices
            .Where(x => x.Kind == NotebookContextSliceKind.SourceChunk)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .ThenBy(x => x.ChunkId, StringComparer.Ordinal)
            .Take(32) // hard cap for trace safety
            .ToList();

        foreach (var c in candidates)
        {
            var sourceId = (c.SourceId ?? string.Empty).Trim();
            var chunkId = (c.ChunkId ?? string.Empty).Trim();
            var candidateId = string.IsNullOrWhiteSpace(chunkId) ? sourceId : chunkId;

            var cand = new ExecutionTraceCandidate
            {
                CandidateId = candidateId,
                Content = Trunc($"[source:{sourceId} chunk:{chunkId}]\n{Trunc(c.Content, 300)}"),
                Score = c.Score
            };
            cand.Metrics["score"] = Cv((double)c.Score);
            cand.Metrics["source_id"] = Cv(sourceId);
            cand.Metrics["chunk_id"] = Cv(chunkId);
            decision.Candidates.Add(cand);
        }

        if (decision.Candidates.Count > 0)
            decision.WinnerCandidateId = decision.Candidates[0].CandidateId;

        if (decision.Candidates.Count > 0)
            node.Decisions.Add(decision);

        return node;
    }

    private static ExecutionTraceNode BuildLlmNode(
        NotebookChatTraceData data,
        Timestamp startedAt,
        Timestamp endedAt)
    {
        var ok = data.Response != null && string.IsNullOrWhiteSpace(data.Error);
        var usage = data.Response?.Usage;

        var node = new ExecutionTraceNode
        {
            NodeId = "llm",
            Name = "generate",
            Type = "llm.generate",
            Status = ok ? ExecutionTraceStatus.Succeeded : ExecutionTraceStatus.Failed,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Description = string.IsNullOrWhiteSpace(data.LlmProvider) ? "LLM generate" : $"LLM generate ({data.LlmProvider})",
            Output = Trunc(data.Response?.Content, 800),
            Error = ok ? string.Empty : (data.Error ?? "llm failed")
        };

        if (usage != null)
        {
            node.Metrics["prompt_tokens"] = Cv(usage.PromptTokens);
            node.Metrics["completion_tokens"] = Cv(usage.CompletionTokens);
            node.Metrics["total_tokens"] = Cv(usage.TotalTokens);
            node.Metrics["estimated_cost"] = Cv(usage.EstimatedCost);
        }

        if (data.Response != null)
        {
            node.Metrics["tool_called"] = Cv(data.Response.ToolCalled);
            if (data.Response.ToolCall != null && !string.IsNullOrWhiteSpace(data.Response.ToolCall.ToolName))
            {
                node.Labels["tool_name"] = data.Response.ToolCall.ToolName.Trim();
            }
        }

        return node;
    }

    private static ExecutionTraceCost BuildCost(NotebookChatTraceData data)
    {
        var usage = data.Response?.Usage;
        return new ExecutionTraceCost
        {
            DurationMs = (long)Math.Max(0, (data.EndedAtUtc - data.StartedAtUtc).TotalMilliseconds),
            TotalLlmCalls = data.Response == null ? 0 : 1,
            TotalTokens = usage?.TotalTokens ?? 0,
            PromptTokens = usage?.PromptTokens ?? 0,
            CompletionTokens = usage?.CompletionTokens ?? 0
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

    private static string Trunc(string? text, int maxChars = 2000)
    {
        var s = (text ?? string.Empty).Replace("\r", "").Trim();
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars];
    }
}

internal sealed record NotebookChatTraceData
{
    public required string ExecutionId { get; init; }
    public required string AgentId { get; init; }
    public required string RequestId { get; init; }
    public required string Query { get; init; }

    public required List<string> SourceIds { get; init; }

    public NotebookContext? Context { get; init; }
    public string? RenderedContext { get; init; }

    public ChatResponse? Response { get; init; }
    public string? Error { get; init; }

    public string? LlmProvider { get; init; }

    public required DateTime StartedAtUtc { get; init; }
    public required DateTime EndedAtUtc { get; init; }
}

internal sealed record NotebookReportTraceData
{
    public required string ExecutionId { get; init; }
    public required string AgentId { get; init; }

    public string? ReportId { get; init; }
    public int Version { get; init; }
    public required string Topic { get; init; }

    public required List<string> SourceIds { get; init; }

    public NotebookContext? Context { get; init; }
    public string? RenderedContext { get; init; }

    public string Outline { get; init; } = string.Empty;
    public string Draft { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;

    public string? Error { get; init; }
    public string? LlmProvider { get; init; }

    public required DateTime StartedAtUtc { get; init; }
    public required DateTime EndedAtUtc { get; init; }
}


