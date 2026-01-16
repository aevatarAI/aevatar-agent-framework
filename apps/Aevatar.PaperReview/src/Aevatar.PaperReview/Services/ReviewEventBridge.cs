using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.PaperReview.Models;
using ProgressEvent = Aevatar.PaperReview.Models.ProgressEvent;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  REVIEW EVENT BRIDGE
//  职责：ReasoningProgress → UI 事件转换
// ============================================================

/// <summary>
/// 评审事件桥接器 - 将 Cognitive 引擎进度转换为前端 UI 事件。
/// </summary>
public sealed class ReviewEventBridge
{
    private readonly ConcurrentDictionary<string, string> _stepContent = new();
    private readonly ConcurrentDictionary<string, string> _stepPrompts = new();
    private readonly ConcurrentDictionary<string, StageTracker> _stageTrackers = new();
    private readonly ConcurrentDictionary<string, int> _streamingTokenIndex = new();
    private readonly ConcurrentDictionary<string, int> _sessionN = new(); // Track N (worker count) per session
    private readonly ConcurrentDictionary<string, int> _sessionK = new(); // Track K (consensus threshold) per session
    
    // ─────────────────────────────────────────────────────────
    //  Atomic Points (PaperReview UI)
    //  - 将 maker-v2 的 subtasks 映射为“atomic point”，用于 UI 展示
    // ─────────────────────────────────────────────────────────
    // NOTE:
    // - 同一个 session 里可能递归调用 maker-v2（Depth 增加）
    // - “当前处于哪个 point”需要按 depth 维护（相当于一个栈）
    // - 这里用 depth -> pointId 的映射来表达“每层正在执行的 workflow_call”
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _sessionActivePointsByDepth = new(); // sessionId -> (pointDepth -> pointId)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionPointTitles = new(); // sessionId -> (pointId -> title/description)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _sessionDecomposeEmitted = new(); // sessionId -> (scopeId -> 0/1) (dedup)
    
    // ─────────────────────────────────────────────────────────
    //  Review Deliverables (Artifacts)
    //  - 每次评审输出两个文件：
    //    1) review_report.md   : compose 后的最终文档（PaperReviewService 已生成）
    //    2) review_details.json: atomic points 每一步 task + consensus（本桥接器聚合）
    // ─────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PointConsensusSnapshot>> _sessionPointConsensus = new(); // sessionId -> (pointId -> consensus)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionPointStatus = new(); // sessionId -> (pointId -> running/completed/failed/pending)

    // Session-level counters (全局统计，不随 StageTracker.Reset() 清零)
    // - countedCallIds: 用 callId 去重，避免重复累加
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _countedCallIds = new();
    private readonly ConcurrentDictionary<string, long> _sessionTotalTokens = new();
    private readonly ConcurrentDictionary<string, int> _sessionTotalLlmCalls = new();
    private readonly ConcurrentDictionary<string, byte> _sessionHasEngineTotals = new(); // 0/1 flag

    private readonly ILogger<ReviewEventBridge> _logger;

    public ReviewEventBridge(ILogger<ReviewEventBridge> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理 Cognitive 引擎的进度事件。
    /// </summary>
    public void HandleProgress(ReviewSession session, ReasoningProgress p)
    {
        var stepId = p.StepId ?? p.TaskId ?? session.Id;
        var phaseText = p.Phase ?? string.Empty;
        var stepType = p.StepType ?? string.Empty;
        var stepStatus = p.StepStatus ?? string.Empty;
        
        // ─────────────────────────────────────────────
        //  Atomic point tracking (for PaperReview UI)
        //  - track which subtask is currently being executed
        //  - emit decompose subtasks as atomic points
        //  - emit per-point consensus conclusion when available
        // ─────────────────────────────────────────────
        TrackAtomicPointContext(session, p, stepId, stepType, stepStatus);
        TryEmitDecomposedPoints(session, p, stepId, stepType, stepStatus);
        TryEmitAtomicPointConsensus(session, p, stepId, stepType, stepStatus);

        // Track N value (worker count) and K value (consensus threshold)
        // N = 2K - 1 (MAKER paper formula)
        // K comes from VoteK (consensus threshold) or calculated from N
        if (p.ParallelTotal.HasValue && p.ParallelTotal.Value > 0)
        {
            var oldN = _sessionN.GetValueOrDefault(session.Id, -1);
            _sessionN[session.Id] = p.ParallelTotal.Value;
            // Calculate K from N: K = (N + 1) / 2
            _sessionK[session.Id] = (p.ParallelTotal.Value + 1) / 2;
            _logger.LogInformation("[BRIDGE] N updated: {OldN} -> {NewN} (ParallelTotal={PT}, StepId={Step})",
                oldN, p.ParallelTotal.Value, p.ParallelTotal.Value, stepId);
        }
        if (p.VoteK.HasValue && p.VoteK.Value > 0)
        {
            var oldK = _sessionK.GetValueOrDefault(session.Id, -1);
            _sessionK[session.Id] = p.VoteK.Value;
            _logger.LogInformation("[BRIDGE] K updated: {OldK} -> {NewK} (VoteK={VK}, StepId={Step})",
                oldK, p.VoteK.Value, p.VoteK.Value, stepId);
        }

        // 缓存 UserPrompt
        if (!string.IsNullOrEmpty(p.UserPrompt))
            _stepPrompts[stepId] = p.UserPrompt;

        // 同步引擎侧统计（如果策略有填充累计 tokens/llmCalls）
        UpdateSessionTotalsFromReasoningProgress(session, p);

        // 处理 Streaming Token（优先，实时显示）
        if (p.StreamingToken != null)
        {
            _logger.LogDebug("[BRIDGE] StreamingToken received: Worker={WorkerId}, First={IsFirst}, Last={IsLast}, Len={Len}",
                p.StreamingToken.WorkerId, p.StreamingToken.IsFirstToken, p.StreamingToken.IsLastToken, 
                p.StreamingToken.AccumulatedContent?.Length ?? 0);
            HandleStreamingToken(session, p);
        }
        else if (stepType == "llm_call")
        {
            _logger.LogDebug("[BRIDGE] LLM step without StreamingToken: StepId={StepId}, Status={Status}",
                stepId, stepStatus);
        }

        // Proposal 也可能包含 token 统计（或至少可估算），用于全局 counters
        // 注意：如果同一个 call 同时走 StreamingToken + Proposal，这里会被 callId 去重
        if (p.Proposal != null)
        {
            TryCountLlmCallFromProposal(session, p);
        }

        // 处理 DSL 步骤事件
        HandleDslStepEvents(session, p, stepId, stepType, stepStatus);

        // 处理 Phase 事件（fallback）
        HandlePhaseEvents(session, p, stepId, phaseText);

        // 处理阶段跟踪和基础进度
        var mappedPhase = PhaseMapper.Map(phaseText, stepType, stepId);
        HandleStageTracking(session, p, mappedPhase);
    }
    
    /// <summary>
    /// Get N (worker count) for Worker ID normalization.
    /// UI displays N worker cards, one per parallel worker.
    /// </summary>
    private int GetN(string sessionId) => _sessionN.GetValueOrDefault(sessionId, 5); // Default worker count is 5

    /// <summary>
    /// Get K (consensus threshold) for voting logic.
    /// Note: Worker ID normalization should use N (worker count), not K (consensus threshold).
    /// </summary>
    private int GetK(string sessionId) => _sessionK.GetValueOrDefault(sessionId, 3); // Default K=3 (for N=5)

    // ─────────────────────────────────────────────────────────
    //  DSL 步骤事件处理
    // ─────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────
    //  Worker ID Normalization
    //  All step IDs are normalized to: "coordinator" or "worker-N"
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalize step ID to logical worker ID.
    /// Uses N (worker count) to cycle workers: gen[N] -> worker-{(N-1) % workerCount}
    /// </summary>
    private static string NormalizeWorkerId(string stepId, int workerCount = 5)
    {
        if (string.IsNullOrEmpty(stepId)) return "coordinator";
        var lower = stepId.ToLowerInvariant();

        // Coordinator patterns
        if (lower.Contains("check_atomic") ||
            lower.Contains("coordinator") ||
            lower == "main" ||
            (lower.Contains("compose") && !lower.Contains("gen[")) ||
            lower.EndsWith(".vote") ||
            lower.Contains("vote"))
        {
            return "coordinator";
        }

        // Worker patterns: gen[N] -> worker-{(N-1) % workerCount} (0-indexed, cyclic)
        var genMatch = System.Text.RegularExpressions.Regex.Match(stepId, @"gen\[(\d+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (genMatch.Success && int.TryParse(genMatch.Groups[1].Value, out var n))
        {
            var workerIndex = workerCount > 0 ? (n - 1) % workerCount : 0; // gen[1]->worker-0, gen[2]->worker-1...
            Console.WriteLine($"[BRIDGE] NormalizeWorkerId: stepId={stepId}, gen[{n}], workerCount={workerCount} -> worker-{workerIndex}");
            return $"worker-{workerIndex}";
        }

        // Already normalized pattern: worker-N -> keep as-is (already 0-indexed)
        var workerMatch = System.Text.RegularExpressions.Regex.Match(stepId, @"^worker[-_]?(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (workerMatch.Success)
        {
            // Already normalized, just return it directly
            return stepId.ToLowerInvariant().Replace("_", "-");
        }

        // UUID-like IDs -> coordinator
        if (stepId.Length > 20 && (stepId.Contains("-") || System.Text.RegularExpressions.Regex.IsMatch(stepId, @"^[a-f0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
        {
            return "coordinator";
        }

        // Default to coordinator
        return "coordinator";
    }

    private static (string workerId, string callId) ResolveWorkerAndCallId(ReasoningProgress p, string fallback, int workerCount)
    {
        var rawId = p.TaskId ?? fallback;
        var workerId = NormalizeWorkerId(rawId, workerCount);
        var callId = p.StreamingToken?.ProposalId != null
            ? $"{workerId}:{p.StreamingToken.ProposalId}"
            : $"{workerId}:{rawId}";
        return (workerId, callId);
    }

    private static (string workerId, string callId) ResolveWorkerAndCallId(StreamingTokenProgress st, string fallbackWorker, int workerCount)
    {
        var rawId = st.WorkerId ?? fallbackWorker;
        var workerId = NormalizeWorkerId(rawId, workerCount);
        var callId = st.ProposalId != null ? $"{workerId}:{st.ProposalId}" : $"{workerId}:{rawId}";
        return (workerId, callId);
    }

    private static (string workerId, string callId) ResolveWorkerAndCallId(ProposalProgress prop, string fallbackWorker, int workerCount)
    {
        var workerId = NormalizeWorkerId(fallbackWorker, workerCount);
        var callId = prop.ProposalId != null ? $"{workerId}:{prop.ProposalId}" : $"{workerId}:{fallbackWorker}";
        return (workerId, callId);
    }

    private static string GetDisplayName(string normalizedWorkerId)
    {
        if (normalizedWorkerId == "coordinator") return "Coordinator";
        var m = System.Text.RegularExpressions.Regex.Match(normalizedWorkerId, @"worker-(\d+)");
        if (m.Success) return $"Worker {m.Groups[1].Value}";
        return normalizedWorkerId;
    }

    // ─────────────────────────────────────────────────────────
    //  Atomic Point Tracking (PaperReview UI)
    // ─────────────────────────────────────────────────────────

    private (string? pointId, string? pointTitle) ResolvePointContext(ReviewSession session, ReasoningProgress p)
    {
        // 约定：
        // - Depth 表示当前在第几层 workflow_call 里执行（root=0）
        // - 一个 point（workflow_call）本身发生在 pointDepth=Depth
        // - point 内部的 llm_call / vote 等步骤发生在 Depth+1，因此“上下文 point”位于 (Depth-1)
        var depth = p.Depth ?? 0;
        if (depth <= 0) return (null, null);

        var pointDepth = depth - 1;
        if (!_sessionActivePointsByDepth.TryGetValue(session.Id, out var activeByDepth) ||
            !activeByDepth.TryGetValue(pointDepth, out var pointId))
        {
            return (null, null);
        }

        var title = _sessionPointTitles.TryGetValue(session.Id, out var titles) &&
                    titles.TryGetValue(pointId, out var t)
            ? t
            : null;

        return (pointId, title);
    }

    private static bool IsAtomicPointStepId(string stepId) =>
        !string.IsNullOrWhiteSpace(stepId) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            stepId,
            @"^execute_subtasks\[\d+\]$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private string ResolveWorkflowCallPointId(ReviewSession session, int pointDepth, string stepId)
    {
        // 顶层：execute_subtasks[i]
        if (pointDepth <= 0) return stepId;

        // 递归层：{parentPointId}/execute_subtasks[i]
        if (_sessionActivePointsByDepth.TryGetValue(session.Id, out var activeByDepth) &&
            activeByDepth.TryGetValue(pointDepth - 1, out var parentPointId) &&
            !string.IsNullOrWhiteSpace(parentPointId))
        {
            return $"{parentPointId}/{stepId}";
        }

        // Fallback：保证唯一性（极端情况下事件乱序/缺失父上下文）
        return $"d{pointDepth}:{stepId}";
    }

    private void TrackAtomicPointContext(
        ReviewSession session, ReasoningProgress p,
        string stepId, string stepType, string stepStatus)
    {
        if (!stepType.Equals("workflow_call", StringComparison.OrdinalIgnoreCase)) return;
        if (!IsAtomicPointStepId(stepId)) return;

        var pointDepth = p.Depth ?? 0; // workflow_call 本身的 Depth 就是 pointDepth
        var pointId = ResolveWorkflowCallPointId(session, pointDepth, stepId);
        var activeByDepth = _sessionActivePointsByDepth.GetOrAdd(
            session.Id,
            _ => new ConcurrentDictionary<int, string>());
        var statusByPoint = _sessionPointStatus.GetOrAdd(
            session.Id,
            _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var statusLower = (stepStatus ?? "").ToLowerInvariant();
        if (statusLower.Contains("running"))
        {
            activeByDepth[pointDepth] = pointId;
            statusByPoint[pointId] = "running";
        }
        else if (statusLower.Contains("completed") || statusLower.Contains("failed") || statusLower.Contains("skipped"))
        {
            activeByDepth.TryRemove(pointDepth, out _);
            
            // 用于 details 输出：记录 point 的最终状态（best-effort）
            if (statusLower.Contains("completed")) statusByPoint[pointId] = "completed";
            else if (statusLower.Contains("failed")) statusByPoint[pointId] = "failed";
            else statusByPoint[pointId] = "pending";

            // 防御性清理：如果更深层有残留上下文，一并清掉（避免 UI “串台”）
            foreach (var d in activeByDepth.Keys)
            {
                if (d > pointDepth)
                    activeByDepth.TryRemove(d, out _);
            }
        }
    }

    private void TryEmitDecomposedPoints(
        ReviewSession session, ReasoningProgress p,
        string stepId, string stepType, string stepStatus)
    {
        if (!stepType.Equals("vote", StringComparison.OrdinalIgnoreCase)) return;
        if (!stepId.Equals("decompose", StringComparison.OrdinalIgnoreCase)) return;

        var statusLower = (stepStatus ?? "").ToLowerInvariant();
        if (!statusLower.Contains("completed")) return;

        // 这个 decompose 属于哪个 point？
        // - root workflow 的 decompose → ParentPointId=null
        // - 子工作流的 decompose → ParentPointId=当前 pointId
        var (parentPointId, parentPointTitle) = ResolvePointContext(session, p);
        var scopeKey = parentPointId ?? "root";

        // Dedup：同一 scope（root 或某个 point）只发一次 subtasks 定义
        var emitted = _sessionDecomposeEmitted.GetOrAdd(session.Id, _ => new ConcurrentDictionary<string, byte>());
        if (!emitted.TryAdd(scopeKey, 0)) return;

        var raw = p.AssistantResponse ?? "";
        var subtasks = TryParseSubtasks(raw);
        if (subtasks.Count == 0)
        {
            // 如果解析失败，允许后续重试（例如先收到 Completed 但 content 为空的边界情况）
            emitted.TryRemove(scopeKey, out _);
            return;
        }

        var titleMap = _sessionPointTitles.GetOrAdd(session.Id, _ => new ConcurrentDictionary<string, string>());
        var subTaskInfos = new List<SubTaskInfo>(subtasks.Count);

        for (var i = 0; i < subtasks.Count; i++)
        {
            var pointId = parentPointId == null
                ? $"execute_subtasks[{i}]"
                : $"{parentPointId}/execute_subtasks[{i}]";
            var desc = subtasks[i];
            titleMap[pointId] = desc;

            subTaskInfos.Add(new SubTaskInfo
            {
                TaskId = pointId,
                Description = desc,
                Status = "pending"
            });
        }

        Emit(session, new TaskDecomposedEvent
        {
            SessionId = session.Id,
            ParentTaskId = stepId,
            ParentPointId = parentPointId,
            ParentPointTitle = parentPointTitle,
            Depth = p.Depth ?? 0,
            Reason = p.Message ?? "",
            SubTasks = subTaskInfos
        });
    }

    private void TryEmitAtomicPointConsensus(
        ReviewSession session, ReasoningProgress p,
        string stepId, string stepType, string stepStatus)
    {
        // 子任务内部的 vote 完成：solve_atomic / compose → 视为该 point 的“共识结论”
        if ((p.Depth ?? 0) <= 0) return;
        if (!stepType.Equals("vote", StringComparison.OrdinalIgnoreCase)) return;

        var statusLower = (stepStatus ?? "").ToLowerInvariant();
        if (!statusLower.Contains("completed")) return;

        // 只采集“子任务最终输出”的 vote（避免把子任务内部 decompose 也当成结论）
        if (!stepId.Equals("solve_atomic", StringComparison.OrdinalIgnoreCase) &&
            !stepId.Equals("compose", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (pointId, pointTitle) = ResolvePointContext(session, p);
        if (string.IsNullOrWhiteSpace(pointId)) return;

        var conclusion = p.AssistantResponse ?? "";
        if (string.IsNullOrWhiteSpace(conclusion)) return;

        // ============================================================
        //  交付物聚合：把每个 point 的最终共识存起来
        //  - 后续由 PaperReviewService 保存为 review_details.json
        // ============================================================
        var consensusByPoint = _sessionPointConsensus.GetOrAdd(
            session.Id,
            _ => new ConcurrentDictionary<string, PointConsensusSnapshot>(StringComparer.OrdinalIgnoreCase));
        consensusByPoint[pointId] = new PointConsensusSnapshot
        {
            PointId = pointId,
            PointTitle = pointTitle,
            SourceStepId = stepId,
            Depth = p.Depth ?? 0,
            Success = true,
            Conclusion = conclusion,
            Timestamp = p.Timestamp
        };

        // best-effort：有共识就视为 completed（即使 workflow_call Completed 事件没到）
        var statusByPoint = _sessionPointStatus.GetOrAdd(
            session.Id,
            _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        statusByPoint[pointId] = "completed";

        Emit(session, new AtomicPointConsensusEvent
        {
            SessionId = session.Id,
            PointId = pointId,
            PointTitle = pointTitle,
            SourceStepId = stepId,
            Depth = p.Depth ?? 0,
            Success = true,
            Conclusion = conclusion
        });
    }

    private static List<string> TryParseSubtasks(string raw)
    {
        // 目标：从 decompose 的 winner content 中提取 subtask.description 列表
        // 容错策略：
        // - 支持 ```json ... ``` code fence
        // - 支持前后夹杂解释文本（截取最外层 [ ... ]）
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            // 去掉首尾 code fence
            var firstNl = s.IndexOf('\n');
            if (firstNl >= 0) s = s[(firstNl + 1)..];
            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) s = s[..lastFence];
            s = s.Trim();
        }

        var start = s.IndexOf('[');
        var end = s.LastIndexOf(']');
        if (start < 0 || end <= start) return [];
        var json = s[start..(end + 1)];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            // ============================================================
            //  解析策略：尽量“按数组长度”返回，避免 UI 出现：
            //  - decompose 只解析出 3 个描述，但 execute_subtasks 实际跑了 5 个 item
            //  - 结果：后两个点只能由 ProgressEvent 补出来 → 标题退化为 execute_subtasks[i]
            // ============================================================
            static string? PickString(JsonElement obj, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (obj.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var text = v.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
                return null;
            }

            static string? FirstStringValue(JsonElement obj)
            {
                foreach (var p in obj.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    var text = p.Value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                return null;
            }

            var list = new List<string>();
            var i = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                i++;

                // 1) 字符串数组：直接当作描述
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString()?.Trim();
                    list.Add(!string.IsNullOrWhiteSpace(text) ? text : $"Subtask {i} (missing description)");
                    continue;
                }

                // 2) 对象数组：多 key 提取（description 优先，其次常见别名）
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var text =
                        PickString(item, "description", "desc", "task", "title", "question", "objective", "content") ??
                        FirstStringValue(item) ??
                        PickString(item, "id"); // 最后兜底：id（但可能只是标识符）

                    // 避免把 execute_subtasks[i] 这种“步骤 ID”当标题（对人无意义）
                    if (string.IsNullOrWhiteSpace(text) || IsAtomicPointStepId(text))
                    {
                        list.Add($"Subtask {i} (missing description)");
                    }
                    else
                    {
                        list.Add(text);
                    }

                    continue;
                }

                // 3) 其他类型：兜底为可读占位
                list.Add($"Subtask {i} (missing description)");
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Deliverables: review_details.json
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 构建“评审明细交付物”（Atomic Points 树 + 共识结论）。
    /// </summary>
    public string BuildReviewDetailsJson(ReviewSession session)
    {
        // NOTE:
        // - 这里不读取前端 cache，而是复用桥接层聚合到的结构化信息
        // - 即使 UI 没打开，也能在 output/ 产出交付物
        var sessionId = session.Id;

        _sessionPointTitles.TryGetValue(sessionId, out var titles);
        _sessionPointConsensus.TryGetValue(sessionId, out var consensusByPoint);
        _sessionPointStatus.TryGetValue(sessionId, out var statusByPoint);

        titles ??= new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        consensusByPoint ??= new ConcurrentDictionary<string, PointConsensusSnapshot>(StringComparer.OrdinalIgnoreCase);
        statusByPoint ??= new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 收集所有 pointId（title/consensus/status 任一来源）
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in titles.Keys) all.Add(id);
        foreach (var id in consensusByPoint.Keys) all.Add(id);
        foreach (var id in statusByPoint.Keys) all.Add(id);

        // 生成节点 & 建树（按 pointId 的 path 规则推导父子关系）
        var nodes = new Dictionary<string, ReviewDetailsPointNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "parent->child"
        var childIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReviewDetailsPointNode EnsureNode(string pointId)
        {
            if (nodes.TryGetValue(pointId, out var existing)) return existing;

            var task = titles.TryGetValue(pointId, out var t) && !string.IsNullOrWhiteSpace(t)
                ? t
                : pointId;

            var status = statusByPoint.TryGetValue(pointId, out var s) && !string.IsNullOrWhiteSpace(s)
                ? s
                : (consensusByPoint.ContainsKey(pointId) ? "completed" : "pending");

            consensusByPoint.TryGetValue(pointId, out var c);

            var node = new ReviewDetailsPointNode
            {
                PointId = pointId,
                Task = task,
                Depth = ComputePointDepth(pointId),
                Status = status,
                Consensus = c == null
                    ? null
                    : new ReviewDetailsPointConsensus
                    {
                        SourceStepId = c.SourceStepId,
                        Success = c.Success,
                        Conclusion = c.Conclusion,
                        Timestamp = c.Timestamp
                    },
                Children = new List<ReviewDetailsPointNode>()
            };

            nodes[pointId] = node;
            return node;
        }

        foreach (var pointId in all)
        {
            // 补齐父链：保证树不会“断”
            var cur = pointId;
            var guard = 0;
            while (!string.IsNullOrWhiteSpace(cur) && guard++ < 32)
            {
                var node = EnsureNode(cur);
                var parentId = ResolveParentPointId(cur);
                if (string.IsNullOrWhiteSpace(parentId)) break;

                var parent = EnsureNode(parentId);

                // 去重边，避免重复挂载
                var edgeKey = $"{parentId}->{cur}";
                if (edges.Add(edgeKey))
                {
                    parent.Children.Add(node);
                    childIds.Add(cur);
                }

                cur = parentId;
            }
        }

        // Roots = 从未作为 child 的节点
        var roots = new List<ReviewDetailsPointNode>();
        foreach (var (id, node) in nodes)
        {
            if (!childIds.Contains(id))
                roots.Add(node);
        }

        // 递归排序：按 execute_subtasks[i] 的 i 排序，其次按 pointId
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void SortRec(ReviewDetailsPointNode n)
        {
            if (!visited.Add(n.PointId)) return;
            n.Children.Sort((a, b) =>
            {
                var ai = ExtractSubtaskIndex(a.PointId);
                var bi = ExtractSubtaskIndex(b.PointId);
                var c = ai.CompareTo(bi);
                return c != 0 ? c : string.Compare(a.PointId, b.PointId, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var ch in n.Children) SortRec(ch);
        }
        roots.Sort((a, b) =>
        {
            var ai = ExtractSubtaskIndex(a.PointId);
            var bi = ExtractSubtaskIndex(b.PointId);
            var c = ai.CompareTo(bi);
            return c != 0 ? c : string.Compare(a.PointId, b.PointId, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var r in roots) SortRec(r);

        var doc = new ReviewDetailsDocument
        {
            SessionId = session.Id,
            Title = session.Title ?? "",
            Authors = session.Authors ?? "",
            VenueType = session.VenueType ?? "",
            ReviewType = session.Type.ToString(),
            CreatedAt = session.CreatedAt,
            GeneratedAt = DateTimeOffset.UtcNow,
            DurationSeconds = session.Duration.TotalSeconds,
            TotalLlmCalls = session.TotalLlmCalls,
            TotalTokens = session.TotalTokens,
            Points = roots
        };

        return JsonSerializer.Serialize(doc, _detailsJsonOptions);
    }

    /// <summary>
    /// 清理 session 相关的桥接缓存（避免进程长期运行导致内存增长）。
    /// </summary>
    public void CleanupSession(string sessionId)
    {
        _stageTrackers.TryRemove(sessionId, out _);
        _sessionN.TryRemove(sessionId, out _);
        _sessionK.TryRemove(sessionId, out _);
        _countedCallIds.TryRemove(sessionId, out _);
        _sessionTotalTokens.TryRemove(sessionId, out _);
        _sessionTotalLlmCalls.TryRemove(sessionId, out _);
        _sessionHasEngineTotals.TryRemove(sessionId, out _);

        _sessionActivePointsByDepth.TryRemove(sessionId, out _);
        _sessionPointTitles.TryRemove(sessionId, out _);
        _sessionDecomposeEmitted.TryRemove(sessionId, out _);
        _sessionPointConsensus.TryRemove(sessionId, out _);
        _sessionPointStatus.TryRemove(sessionId, out _);
    }

    // ─────────────────────────────────────────────────────────
    //  helpers (details)
    // ─────────────────────────────────────────────────────────

    private static int ComputePointDepth(string pointId)
    {
        if (string.IsNullOrWhiteSpace(pointId)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(
            pointId,
            @"^d(\d+):",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var d)) return d;

        var depth = 0;
        foreach (var ch in pointId)
        {
            if (ch == '/') depth++;
        }
        return depth;
    }

    private static string? ResolveParentPointId(string pointId)
    {
        if (string.IsNullOrWhiteSpace(pointId)) return null;
        var slash = pointId.LastIndexOf('/');
        return slash > 0 ? pointId[..slash] : null;
    }

    private static int ExtractSubtaskIndex(string pointId)
    {
        if (string.IsNullOrWhiteSpace(pointId)) return int.MaxValue;
        var m = System.Text.RegularExpressions.Regex.Match(
            pointId,
            @"execute_subtasks\[(\d+)\]$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var i) ? i : int.MaxValue;
    }

    private static readonly JsonSerializerOptions _detailsJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record ReviewDetailsDocument
    {
        public string SessionId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Authors { get; init; } = "";
        public string VenueType { get; init; } = "";
        public string ReviewType { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset GeneratedAt { get; init; }
        public double DurationSeconds { get; init; }
        public int TotalLlmCalls { get; init; }
        public long TotalTokens { get; init; }
        public List<ReviewDetailsPointNode> Points { get; init; } = [];
    }

    private sealed record ReviewDetailsPointNode
    {
        public string PointId { get; init; } = "";
        public string Task { get; init; } = "";
        public int Depth { get; init; }
        public string Status { get; init; } = "pending";
        public ReviewDetailsPointConsensus? Consensus { get; init; }
        public List<ReviewDetailsPointNode> Children { get; init; } = [];
    }

    private sealed record ReviewDetailsPointConsensus
    {
        public string SourceStepId { get; init; } = "";
        public bool Success { get; init; }
        public string Conclusion { get; init; } = "";
        public DateTimeOffset Timestamp { get; init; }
    }

    private sealed record PointConsensusSnapshot
    {
        public string PointId { get; init; } = "";
        public string? PointTitle { get; init; }
        public string SourceStepId { get; init; } = "";
        public int Depth { get; init; }
        public bool Success { get; init; }
        public string Conclusion { get; init; } = "";
        public DateTimeOffset Timestamp { get; init; }
    }

    private void HandleDslStepEvents(
        ReviewSession session, ReasoningProgress p,
        string stepId, string stepType, string stepStatus)
    {
        if (string.IsNullOrEmpty(stepType)) return;

        var statusLower = stepStatus.ToLowerInvariant();

        // Skip llm_call if StreamingToken is present - handled by HandleStreamingToken
        if (stepType.Equals("llm_call", StringComparison.OrdinalIgnoreCase))
        {
            if (p.StreamingToken != null)
            {
                // StreamingToken path handles all LLM events
                return;
            }
            _logger.LogDebug("[DSL] llm_call (no streaming): stepId={StepId}, status={Status}",
                stepId, stepStatus);
            HandleLlmCallStep(session, p, stepId, statusLower);
        }
        else if (stepType.Equals("vote", StringComparison.OrdinalIgnoreCase))
        {
            HandleVoteStep(session, p, stepId, statusLower);
        }
    }

    private void HandleLlmCallStep(
        ReviewSession session, ReasoningProgress p,
        string stepId, string statusLower)
    {
        var (workerId, callId) = ResolveWorkerAndCallId(p, stepId, GetN(session.Id));
        if (statusLower.Contains("running"))
        {
            var startKey = $"{session.Id}:{workerId}:started";
            var isFirstEvent = !_stepContent.ContainsKey(startKey);

            if (isFirstEvent)
            {
                _logger.LogInformation("[LLM] ➡️ Start: {Worker}", workerId);
                _stepContent[startKey] = "1";
                EmitWorkerStart(session, workerId, p);
                EmitLlmCallStart(session, workerId, callId, p);
            }
        }
        else if (statusLower.Contains("completed"))
        {
            var preview = p.AssistantResponse ?? p.Message ?? "(llm done)";
            _stepContent[workerId] = preview;
            _stepContent.TryRemove($"{session.Id}:{workerId}:started", out _);

            EmitLlmCallComplete(session, workerId, callId, preview, p);
            EmitWorkerComplete(session, workerId, preview, p);
        }
    }

    private void HandleVoteStep(
        ReviewSession session, ReasoningProgress p,
        string stepId, string statusLower)
    {
        var leaderVotes = p.VoteCurrentVotes ?? 0;
        var votesNeeded = p.VoteK ?? 0;

        EmitVotingRound(session, stepId, p.VoteRound ?? 0, votesNeeded, leaderVotes, p.Message);

        if (statusLower.Contains("completed") || (votesNeeded > 0 && leaderVotes >= votesNeeded))
        {
            EmitConsensus(session, p.VoteRound ?? 0, leaderVotes);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Phase 事件处理（fallback）
    // ─────────────────────────────────────────────────────────

    private void HandlePhaseEvents(
        ReviewSession session, ReasoningProgress p,
        string stepId, string phaseText)
    {
        if (string.IsNullOrEmpty(phaseText) || !string.IsNullOrEmpty(p.StepType))
            return;

        // Normalize worker ID for all phase events
        var workerId = NormalizeWorkerId(stepId, GetN(session.Id));
        var callId = $"{workerId}:{stepId}";

        if (phaseText.StartsWith("LLM Call", StringComparison.OrdinalIgnoreCase))
        {
            EmitWorkerStart(session, workerId, p);
            EmitLlmCallStart(session, workerId, callId, p);
        }
        else if (phaseText.StartsWith("LLM Done", StringComparison.OrdinalIgnoreCase))
        {
            var preview = p.Message ?? "(llm done)";
            _stepContent[workerId] = preview;
            EmitLlmCallComplete(session, workerId, callId, preview, p);
            EmitWorkerComplete(session, workerId, preview, p);
        }
        else if (phaseText.StartsWith("Voting", StringComparison.OrdinalIgnoreCase) && p.Voting != null)
        {
            var v = p.Voting;
            EmitVotingRound(session, workerId, v.Round, v.VotesNeeded, v.LeaderVotes, null, v.Type);
            if (v.LeaderVotes >= v.VotesNeeded)
            {
                EmitConsensus(session, v.Round, v.LeaderVotes);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  阶段跟踪
    // ─────────────────────────────────────────────────────────

    private void HandleStageTracking(
        ReviewSession session, ReasoningProgress p, ReviewPhase mappedPhase)
    {
        var phaseStr = mappedPhase.ToString();
        session.Timeline.Add(new TimelineEntry(phaseStr, p.Message ?? "", DateTimeOffset.UtcNow));
        var previousPhase = session.CurrentPhase;
        session.CurrentPhase = phaseStr;

        var tracker = _stageTrackers.GetOrAdd(session.Id, _ => new StageTracker());

        // Voting is a sub-phase within main stages, don't switch stage for it
        // Main stages: Assessing, Decomposing, Solving, Composing
        // Sub-phases: Voting, Starting, Executing
        var isMainStage = mappedPhase is ReviewPhase.Assessing or ReviewPhase.Decomposing 
            or ReviewPhase.Solving or ReviewPhase.Composing or ReviewPhase.Completed or ReviewPhase.Failed;
        var currentIsMainStage = tracker.CurrentStage is "Assessing" or "Decomposing" 
            or "Solving" or "Composing" or "Completed" or "Failed";
        
        // Don't switch from a main stage to a sub-phase (Voting)
        if (currentIsMainStage && !isMainStage)
        {
            // Stay in current main stage, just update voting progress
            // Don't emit stage change events
        }
        else
        {
            // 阶段变更 - 完成上一阶段 (only for main stage transitions)
            if (!string.IsNullOrEmpty(tracker.CurrentStage) && tracker.CurrentStage != phaseStr && isMainStage)
        {
            EmitStageComplete(session, tracker);
            tracker.Reset();
        }

            // 新阶段开始 (only for main stages)
            if (tracker.CurrentStage != phaseStr && isMainStage)
        {
            tracker.CurrentStage = phaseStr;
            tracker.StageStartTime = DateTimeOffset.UtcNow;
            EmitStageStart(session, phaseStr);
            }
        }

        // 基础进度事件
        EmitProgress(session, p, phaseStr, tracker);
        EmitPhaseChange(session, p, previousPhase, phaseStr);

        // 投票进度
        if (p.Voting != null)
            HandleVotingProgress(session, p.Voting, tracker);
        
        // vote 完成时，把 winner content 记录到 StageDetails（便于 UI/诊断）
        // 注意：vote 的 winner content 由引擎侧透出到 AssistantResponse
        if (p.StepType?.Equals("vote", StringComparison.OrdinalIgnoreCase) == true &&
            (p.StepStatus?.ToLowerInvariant().Contains("completed") == true) &&
            !string.IsNullOrWhiteSpace(p.AssistantResponse))
        {
            tracker.WinnerContent = p.AssistantResponse;
        }

        // Proposal 完成 - 更新 tracker 统计
        if (p.Proposal != null)
        {
            // 更新 tracker 统计（用于 StageDetails）
            UpdateTrackerFromProposal(session, p, tracker);
            
            // 仅当没有 StreamingToken 时发送 worker 事件
            // StreamingToken 路径已经发送了所有 worker 事件
            if (p.StreamingToken == null)
            HandleProposalComplete(session, p, tracker);
        }
        
        // 也从 StreamingToken 更新 tracker
        // - 如果同一个 Progress 同时带 Proposal + StreamingToken（常见于 Cognitive DSL），
        //   Proposal 已经包含完整内容/统计，这里避免双计数。
        if (p.StreamingToken != null && p.StreamingToken.IsLastToken && p.Proposal == null)
        {
            UpdateTrackerFromStreaming(session, p, tracker);
        }
    }

    private void UpdateTrackerFromProposal(ReviewSession session, ReasoningProgress p, StageTracker tracker)
    {
        var prop = p.Proposal!;
        var promptTokens = prop.PromptTokens ?? 0;
        var completionTokens = prop.CompletionTokens ?? 0;
        var totalTokens = promptTokens + completionTokens;

        // 某些策略（例如 Cognitive DSL）不会返回 token 统计，这里做一个一致的粗略估算
        if (totalTokens == 0 && !string.IsNullOrEmpty(prop.Content))
            totalTokens = Math.Max(1, prop.Content.Length / 4);
        
        tracker.LlmCalls++;
        tracker.Tokens += totalTokens;
        tracker.WorkerCount = Math.Max(tracker.WorkerCount, 1);
        
        if (!string.IsNullOrEmpty(prop.Content))
        {
            var workerId = NormalizeWorkerId(p.TaskId ?? p.StepId ?? "unknown", GetN(session.Id));
            tracker.WorkerOutputs.Add(new WorkerOutput
            {
                WorkerId = workerId,
                Preview = Truncate(prop.Content, 200),
                Content = prop.Content,
                Tokens = totalTokens,
                Success = prop.Success
            });
        }
    }

    private void UpdateTrackerFromStreaming(ReviewSession session, ReasoningProgress p, StageTracker tracker)
    {
        var st = p.StreamingToken!;
        var tokenCount = st.TokenIndex;
        if (tokenCount <= 0 && !string.IsNullOrEmpty(st.AccumulatedContent))
            tokenCount = Math.Max(1, st.AccumulatedContent.Length / 4);

        tracker.LlmCalls++;
        tracker.Tokens += tokenCount;
        tracker.WorkerCount = Math.Max(tracker.WorkerCount, 1);
        
        if (!string.IsNullOrEmpty(st.AccumulatedContent))
        {
            var workerId = NormalizeWorkerId(st.WorkerId ?? p.TaskId ?? "unknown", GetN(session.Id));
            tracker.WorkerOutputs.Add(new WorkerOutput
            {
                WorkerId = workerId,
                Preview = Truncate(st.AccumulatedContent, 200),
                Content = st.AccumulatedContent,
                Tokens = tokenCount,
                Success = true
            });
        }
    }

    private void HandleVotingProgress(
        ReviewSession session, VotingProgress v, StageTracker tracker)
    {
        tracker.VotingRounds = Math.Max(tracker.VotingRounds, v.Round);
        tracker.WorkerCount = Math.Max(tracker.WorkerCount, v.TotalVotes);

        tracker.Candidates.Clear();
        tracker.Candidates.Add(new CandidateDetail
        {
            Id = "Leader",
            Preview = $"Leading candidate with {v.LeaderVotes} votes",
            Votes = v.LeaderVotes,
            IsWinner = v.LeaderVotes >= v.VotesNeeded
        });

        Emit(session, new VotingRoundEvent
        {
            SessionId = session.Id,
            VotingType = v.Type,
            Round = v.Round,
            VotesNeeded = v.VotesNeeded,
            ConsensusReached = v.LeaderVotes >= v.VotesNeeded,
            Candidates =
            [
                new VoteCandidateInfo { CandidateId = "Leader", Votes = v.LeaderVotes, IsLeader = true },
                new VoteCandidateInfo { CandidateId = "RunnerUp", Votes = v.RunnerUpVotes, IsLeader = false }
            ]
        });

        if (v.LeaderVotes >= v.VotesNeeded)
        {
            tracker.ConsensusReached = true;
            Emit(session, new ConsensusEvent
            {
                SessionId = session.Id,
                Round = v.Round,
                TotalVotes = v.TotalVotes,
                VotesNeeded = v.VotesNeeded,
                LeaderVotes = v.LeaderVotes,
                Reached = true
            });
        }
    }

    private void HandleStreamingToken(ReviewSession session, ReasoningProgress p)
    {
        var st = p.StreamingToken!;
        var fallback = p.TaskId ?? st.WorkerId ?? session.Id;
        var n = GetN(session.Id);
        var (workerId, callId) = ResolveWorkerAndCallId(st, fallback, n);
        var display = GetDisplayName(workerId);
        var (pointId, pointTitle) = ResolvePointContext(session, p);
        
        // Track streaming state per call
        var streamKey = $"{session.Id}:{callId}:stream";
        var isFirstForThisCall = !_stepContent.ContainsKey(streamKey);
        var tokenIndex = _streamingTokenIndex.AddOrUpdate(streamKey, 1, (_, v) => v + 1);
        var tokenCount = st.TokenIndex > 0 ? st.TokenIndex : tokenIndex; // 优先使用策略提供的 TokenIndex（更像 token 数）

        if (isFirstForThisCall)
        {
            _stepContent[streamKey] = "1";
            _logger.LogInformation("[STREAMING] ▶ Start: Worker={WorkerId}, CallId={CallId}", workerId, callId);
            _logger.LogDebug(
                "[STREAM-MAP] proposal={ProposalId} fallback={Fallback} n={N} -> workerId={WorkerId} display={Display}",
                st.ProposalId ?? p.StepId ?? "",
                fallback,
                n,
                workerId,
                display);

            Emit(session, new WorkerStartedEvent
            {
                SessionId = session.Id,
                WorkerId = workerId,
                TaskId = workerId,
                DisplayName = display,
                Role = p.Phase ?? "LLM",
                ProviderName = st.ProviderName,
                PointId = pointId,
                PointTitle = pointTitle
            });

            Emit(session, new LlmCallStartEvent
            {
                SessionId = session.Id,
                CallId = callId,
                WorkerId = workerId,
                DisplayName = display,
                ProviderName = st.ProviderName,
                SystemPrompt = st.SystemPrompt,
                UserPrompt = st.UserPrompt,
                Phase = p.Phase ?? "LLM",
                PointId = pointId,
                PointTitle = pointTitle
            });
        }

        // Emit streaming event for every token
        _logger.LogDebug("[STREAMING] 📤 Emit: Worker={WorkerId}, Token={TokenIndex}, First={IsFirst}, Last={IsLast}, Len={Len}",
            workerId, tokenIndex, isFirstForThisCall, st.IsLastToken, st.AccumulatedContent?.Length ?? 0);

        Emit(session, new LlmStreamingEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = display,
            Token = st.Token,
            AccumulatedContent = st.AccumulatedContent,
            TokenIndex = tokenCount,
            IsFirstToken = isFirstForThisCall,
            IsLastToken = st.IsLastToken,
            PointId = pointId,
            PointTitle = pointTitle
        });

        // When streaming completes, emit completion event
        if (st.IsLastToken)
        {
            _logger.LogInformation("[STREAMING] ✓ Complete: Worker={WorkerId}, Tokens={TokenIndex}", workerId, tokenCount);
            _stepContent.TryRemove(streamKey, out _);
            _streamingTokenIndex.TryRemove(streamKey, out _);

            // 全局统计：只在完成时计一次（callId 去重）
            CountLlmCall(session, callId, promptTokens: 0, completionTokens: tokenCount);

            Emit(session, new LlmCallCompleteEvent
            {
                SessionId = session.Id,
                CallId = callId,
                WorkerId = workerId,
                DisplayName = display,
                Success = true,
                Content = st.AccumulatedContent ?? "",
                PromptTokens = 0,
                CompletionTokens = tokenCount,
                LatencyMs = 0,
                ProviderName = st.ProviderName,
                Phase = p.Phase ?? "LLM",
                PointId = pointId,
                PointTitle = pointTitle
            });
        }
    }

    private void HandleProposalComplete(
        ReviewSession session, ReasoningProgress p, StageTracker tracker)
    {
        var prop = p.Proposal!;
        var promptTokens = prop.PromptTokens ?? 0;
        var completionTokens = prop.CompletionTokens ?? 0;
        var totalTokens = promptTokens + completionTokens;
        if (totalTokens == 0 && !string.IsNullOrEmpty(prop.Content))
            totalTokens = Math.Max(1, prop.Content.Length / 4);
        var (workerId, callId) = ResolveWorkerAndCallId(prop, p.TaskId ?? session.Id, GetN(session.Id));
        var (pointId, pointTitle) = ResolvePointContext(session, p);

        _logger.LogInformation("[PROPOSAL_COMPLETE] Worker={WorkerId}, Tokens={Tokens}",
            workerId, totalTokens);

        // 全局统计：计一次（callId 去重）
        // - 如果策略提供了 Prompt/CompletionTokens 则用真实值
        // - 否则用 content 长度估算（与 CognitiveStrategy 对齐）
        if (promptTokens + completionTokens > 0)
            CountLlmCall(session, callId, promptTokens, completionTokens);
        else
            CountLlmCall(session, callId, promptTokens: 0, completionTokens: totalTokens);

        Emit(session, new WorkerStartedEvent
        {
            SessionId = session.Id,
            WorkerId = workerId,
            TaskId = p.TaskId ?? session.Id,
            DisplayName = GetDisplayName(workerId),
            Role = p.Phase ?? "LLM",
            ProviderName = prop.ProviderName,
            PointId = pointId,
            PointTitle = pointTitle
        });

        Emit(session, new LlmCallStartEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = GetDisplayName(workerId),
            ProviderName = prop.ProviderName,
            Phase = p.Phase ?? "LLM",
            PointId = pointId,
            PointTitle = pointTitle
        });

        Emit(session, new WorkerCompletedEvent
        {
            SessionId = session.Id,
            WorkerId = workerId,
            TaskId = p.TaskId ?? session.Id,
            DisplayName = GetDisplayName(workerId),
            Success = prop.Success,
            ContentPreview = Truncate(prop.Content, 100),
            Content = prop.Content ?? "",
            LatencyMs = 0,
            TotalTokens = totalTokens,
            PointId = pointId,
            PointTitle = pointTitle
        });

        Emit(session, new LlmCallCompleteEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = GetDisplayName(workerId),
            Success = prop.Success,
            Content = prop.Content ?? "",
            Error = prop.Error,
            PromptTokens = promptTokens,
            CompletionTokens = (promptTokens + completionTokens) > 0 ? completionTokens : totalTokens,
            LatencyMs = 0,
            ProviderName = prop.ProviderName,
            Phase = p.Phase ?? "LLM",
            PointId = pointId,
            PointTitle = pointTitle
        });
    }

    // ─────────────────────────────────────────────────────────
    //  事件发射辅助方法
    // ─────────────────────────────────────────────────────────

    private void EmitWorkerStart(ReviewSession session, string workerId, ReasoningProgress p)
    {
        var (pointId, pointTitle) = ResolvePointContext(session, p);
        Emit(session, new WorkerStartedEvent
        {
            SessionId = session.Id,
            WorkerId = workerId,
            TaskId = workerId,
            DisplayName = GetDisplayName(workerId),
            Role = "LLM",
            ProviderName = "deepseek",
            PointId = pointId,
            PointTitle = pointTitle
        });
    }

    private void EmitLlmCallStart(ReviewSession session, string workerId, string callId, ReasoningProgress p)
    {
        var (pointId, pointTitle) = ResolvePointContext(session, p);
        Emit(session, new LlmCallStartEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = GetDisplayName(workerId),
            ProviderName = "deepseek",
            SystemPrompt = p.SystemPrompt,
            UserPrompt = p.UserPrompt ?? _stepPrompts.GetValueOrDefault(workerId) ?? p.Message ?? "(llm_call)",
            Phase = "LLM",
            PointId = pointId,
            PointTitle = pointTitle
        });
    }

    private void EmitLlmStreaming(ReviewSession session, string workerId, string callId, string content)
    {
        Emit(session, new LlmStreamingEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = GetDisplayName(workerId),
            Token = "",
            AccumulatedContent = content
        });
    }

    private void EmitLlmCallComplete(ReviewSession session, string workerId, string callId, string content, ReasoningProgress p)
    {
        var (pointId, pointTitle) = ResolvePointContext(session, p);
        Emit(session, new LlmCallCompleteEvent
        {
            SessionId = session.Id,
            CallId = callId,
            WorkerId = workerId,
            DisplayName = GetDisplayName(workerId),
            Success = true,
            Content = content,
            PromptTokens = 0,
            CompletionTokens = 0,
            LatencyMs = 0,
            ProviderName = "deepseek",
            Phase = "LLM",
            PointId = pointId,
            PointTitle = pointTitle
        });
    }

    private void EmitWorkerComplete(ReviewSession session, string stepId, string content, ReasoningProgress p)
    {
        var (pointId, pointTitle) = ResolvePointContext(session, p);
        Emit(session, new WorkerCompletedEvent
        {
            SessionId = session.Id,
            WorkerId = stepId,
            TaskId = stepId,
            Success = true,
            Content = content,
            ContentPreview = Truncate(content, 100),
            LatencyMs = 0,
            TotalTokens = 0,
            PointId = pointId,
            PointTitle = pointTitle
        });
    }

    private void EmitVotingRound(
        ReviewSession session, string stepId, int round,
        int votesNeeded, int leaderVotes, string? message, string? votingType = null)
    {
        Emit(session, new VotingRoundEvent
        {
            SessionId = session.Id,
            TaskId = stepId,
            VotingType = votingType ?? "",
            Round = round,
            VotesNeeded = votesNeeded,
            Candidates =
            [
                new VoteCandidateInfo
                {
                    CandidateId = "leader",
                    ContentPreview = message ?? "(leader)",
                    Votes = leaderVotes,
                    IsLeader = true
                }
            ],
            ConsensusReached = votesNeeded > 0 && leaderVotes >= votesNeeded
        });
    }

    private void EmitConsensus(ReviewSession session, int round, int leaderVotes)
    {
        Emit(session, new ConsensusEvent
        {
            SessionId = session.Id,
            Round = round,
            LeaderVotes = leaderVotes,
            TotalVotes = leaderVotes
        });
    }

    private void EmitStageStart(ReviewSession session, string phase)
    {
        Emit(session, new StageLogEvent
        {
            SessionId = session.Id,
            Stage = phase,
            Status = "started",
            Summary = $"Stage '{phase}' started",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow
        });
    }

    private void EmitStageComplete(ReviewSession session, StageTracker tracker)
    {
        var endTime = DateTimeOffset.UtcNow;
        var duration = (long)(endTime - tracker.StageStartTime).TotalMilliseconds;

        Emit(session, new StageLogEvent
        {
            SessionId = session.Id,
            Stage = tracker.CurrentStage ?? "Unknown",
            Status = "completed",
            Summary = BuildStageSummary(tracker.CurrentStage ?? "Unknown", tracker),
            StartTime = tracker.StageStartTime,
            EndTime = endTime,
            DurationMs = duration,
            Stats = new StageStats
            {
                LlmCalls = tracker.LlmCalls,
                Tokens = tracker.Tokens,
                WorkerCount = tracker.WorkerCount,
                VotingRounds = tracker.VotingRounds,
                ConsensusReached = tracker.ConsensusReached
            },
            Details = new StageDetails
            {
                Candidates = tracker.Candidates.Count > 0 ? [.. tracker.Candidates] : null,
                WorkerOutputs = tracker.WorkerOutputs.Count > 0 ? [.. tracker.WorkerOutputs] : null,
                WinnerContent = tracker.WinnerContent
            }
        });
    }

    private void EmitProgress(ReviewSession session, ReasoningProgress p, string phase, StageTracker tracker)
    {
        // Attach atomic point context for UI (mind-map)
        string? pointId = null;
        string? pointTitle = null;
        
        var rawStepId = p.StepId ?? "";
        var rawStepType = p.StepType ?? "";
        var depth = p.Depth ?? 0;
        
        // workflow_call 本身就是一个 point 节点（pointDepth=Depth）
        if (rawStepType.Equals("workflow_call", StringComparison.OrdinalIgnoreCase) &&
            IsAtomicPointStepId(rawStepId))
        {
            pointId = ResolveWorkflowCallPointId(session, depth, rawStepId);
            if (_sessionPointTitles.TryGetValue(session.Id, out var titles) &&
                titles.TryGetValue(pointId, out var t))
            {
                pointTitle = t;
            }
        }
        else
        {
            // 其他步骤（llm_call / vote / fan_out 等）归属到“上一层 point”
            (pointId, pointTitle) = ResolvePointContext(session, p);
        }

        Emit(session, new ProgressEvent
        {
            SessionId = session.Id,
            Phase = phase,
            Message = p.Message ?? "",
            ProgressPercent = session.ProgressPercent,
            Depth = p.Depth ?? 0,
            // DSL 步骤信息
            StepId = p.StepId,
            StepType = p.StepType,
            StepStatus = p.StepStatus,
            // Atomic Point
            PointId = pointId,
            PointTitle = pointTitle,
            // Vote 相关
            VoteRound = p.VoteRound ?? 0,
            VoteMaxRounds = p.VoteMaxRounds ?? 0,
            VoteK = p.VoteK ?? 0,
            VoteCurrentVotes = p.VoteCurrentVotes ?? 0,
            // Parallel 相关
            ParallelTotal = p.ParallelTotal ?? 0,
            ParallelCompleted = p.ParallelCompleted ?? 0,
            ParallelFailed = p.ParallelFailed ?? 0,
            // 统计信息
            TotalLlmCalls = session.TotalLlmCalls,
            TotalTokens = session.TotalTokens
        });
    }

    // ─────────────────────────────────────────────────────────
    //  Session-level Totals (全局统计)
    // ─────────────────────────────────────────────────────────

    private void UpdateSessionTotalsFromReasoningProgress(ReviewSession session, ReasoningProgress p)
    {
        if (p.TotalLlmCalls is { } totalCalls)
        {
            _sessionHasEngineTotals.TryAdd(session.Id, 1);
            if (totalCalls > session.TotalLlmCalls)
            {
                session.TotalLlmCalls = totalCalls;
                _sessionTotalLlmCalls.AddOrUpdate(session.Id, totalCalls, (_, current) => Math.Max(current, totalCalls));
            }
        }

        var totalTokens = (p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0);
        if (p.TotalPromptTokens is { } || p.TotalCompletionTokens is { })
        {
            _sessionHasEngineTotals.TryAdd(session.Id, 1);
        }

        if (totalTokens > session.TotalTokens)
        {
            session.TotalTokens = totalTokens;
            _sessionTotalTokens.AddOrUpdate(session.Id, totalTokens, (_, current) => Math.Max(current, totalTokens));
        }
    }

    private void TryCountLlmCallFromProposal(ReviewSession session, ReasoningProgress p)
    {
        var prop = p.Proposal;
        if (prop == null) return;

        var promptTokens = prop.PromptTokens ?? 0;
        var completionTokens = prop.CompletionTokens ?? 0;
        var totalTokens = promptTokens + completionTokens;
        if (totalTokens == 0 && !string.IsNullOrEmpty(prop.Content))
            totalTokens = Math.Max(1, prop.Content.Length / 4);

        var (workerId, callId) = ResolveWorkerAndCallId(prop, p.TaskId ?? session.Id, GetN(session.Id));
        _ = workerId; // callId 内已包含规范化 workerId，用于去重
        if (promptTokens + completionTokens > 0)
            CountLlmCall(session, callId, promptTokens, completionTokens);
        else
            CountLlmCall(session, callId, promptTokens: 0, completionTokens: totalTokens);
    }

    private void CountLlmCall(ReviewSession session, string callId, int promptTokens, int completionTokens)
    {
        // If engine provides authoritative cumulative totals, don't double-count here.
        if (_sessionHasEngineTotals.ContainsKey(session.Id)) return;
        if (string.IsNullOrWhiteSpace(callId)) return;

        var counted = _countedCallIds.GetOrAdd(session.Id, _ => new ConcurrentDictionary<string, byte>());
        if (!counted.TryAdd(callId, 0)) return;

        // Baseline: 如果 session 已经有累计值（例如从引擎最终统计回填），先同步到字典
        _sessionTotalLlmCalls.TryAdd(session.Id, session.TotalLlmCalls);
        _sessionTotalTokens.TryAdd(session.Id, session.TotalTokens);

        _sessionTotalLlmCalls.AddOrUpdate(session.Id, 1, (_, current) => current + 1);

        var deltaTokens = (long)promptTokens + completionTokens;
        _sessionTotalTokens.AddOrUpdate(session.Id, deltaTokens, (_, current) => current + deltaTokens);

        session.TotalLlmCalls = _sessionTotalLlmCalls[session.Id];
        session.TotalTokens = _sessionTotalTokens[session.Id];
    }

    private void EmitPhaseChange(ReviewSession session, ReasoningProgress p, string oldPhase, string newPhase)
    {
        Emit(session, new PhaseChangeEvent
        {
            SessionId = session.Id,
            TaskId = p.TaskId ?? session.Id,
            OldPhase = oldPhase,
            NewPhase = newPhase,
            Message = p.Message ?? ""
        });
    }

    private static void Emit(ReviewSession session, ReviewEvent evt) =>
        session.EventChannel.Writer.TryWrite(evt);

    private static string Truncate(string? s, int maxLen) =>
        s == null ? "" : s.Length > maxLen ? s[..maxLen] + "..." : s;

    private static string BuildStageSummary(string stage, StageTracker tracker)
    {
        var parts = new List<string>();
        if (tracker.LlmCalls > 0) parts.Add($"{tracker.LlmCalls} LLM calls");
        if (tracker.Tokens > 0) parts.Add($"{tracker.Tokens:N0} tokens");
        if (tracker.WorkerCount > 0) parts.Add($"{tracker.WorkerCount} workers");
        if (tracker.VotingRounds > 0) parts.Add($"{tracker.VotingRounds} voting rounds");
        if (tracker.ConsensusReached) parts.Add("✓ consensus");

        var details = parts.Count > 0 ? string.Join(", ", parts) : "completed";

        return stage switch
        {
            "Starting" => $"Initialization complete. {details}",
            "Assessing" => $"Task complexity assessed. {details}",
            "Decomposing" => $"Task decomposed into subtasks. {details}",
            "Voting" => $"Voting consensus process. {details}",
            "Solving" => $"Atomic task solved. {details}",
            "Composing" => $"Results composed. {details}",
            "Completed" => $"Review completed successfully. {details}",
            "Failed" => $"Stage failed. {details}",
            _ => $"Stage '{stage}' completed. {details}"
        };
    }
}