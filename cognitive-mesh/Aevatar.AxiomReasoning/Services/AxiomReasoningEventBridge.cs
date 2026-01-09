using Aevatar.AxiomReasoning.Models;
using ReasoningProgress = Aevatar.CognitiveMesh.Abstractions.ReasoningProgress;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Aevatar.AxiomReasoning.Graph;
using Aevatar.AxiomReasoning.EventStreaming.Events;
using System.Collections.Generic;
using System.IO;

namespace Aevatar.AxiomReasoning.Services;

// ============================================================
//  EVENT BRIDGE
//  职责：ReasoningProgress → Session 状态 + SSE 事件
// ============================================================

public sealed class AxiomReasoningEventBridge
{
    // IMPORTANT:
    // - 想要“PaperReview 那种丝滑”，必须走增量：前端 append tokenDelta，而不是每次替换全文。
    // - 这里用“累积内容差分”计算 delta（因为上游未必提供真实 token 字符串）。
    // - 完成态仍推送完整正文（可下载/可追溯）。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> StreamLastLen = new();
    private const int MaxBodyChars = 20_000;

    private readonly ILogger<AxiomReasoningEventBridge> _logger;
    private readonly IGraphStore _graphStore;

    public AxiomReasoningEventBridge(
        ILogger<AxiomReasoningEventBridge> logger,
        IGraphStore graphStore)
    {
        _logger = logger;
        _graphStore = graphStore;
    }

    public void HandleProgress(AxiomSession session, ReasoningProgress p)
    {
        // IMPORTANT:
        // - Progress<T> 的回调可能在 ThreadPool 上执行
        // - 任何异常都会变成“未处理异常”并直接杀死进程
        // - 这里必须是边界层：永远不要抛异常
        if (session is null)
        {
            _logger.LogWarning("[AXIOM] Ignore progress: session is null");
            return;
        }

        if (p is null)
        {
            _logger.LogWarning("[AXIOM] Ignore progress: progress is null (session={SessionId})", session.Id);
            return;
        }

        try
        {
            // 更新 session 基础状态（供 status API 使用）
            session.CurrentPhase = p.Phase;

            var percent = Math.Clamp((int)(p.ProgressPercent * 100), 0, 100);
            session.ProgressPercent = percent;

            // 引擎累计统计（如果有填充）
            if (p.TotalLlmCalls.HasValue) session.TotalLlmCalls = p.TotalLlmCalls.Value;

            if (p.TotalPromptTokens.HasValue || p.TotalCompletionTokens.HasValue)
            {
                session.TotalTokens = (p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0);
            }

            // 时间线（压缩：只记录关键阶段变化）
            // NOTE:
            // - Progress 事件可能并发触发
            // - List 里理论上不该有 null，但边界层必须防御一切异常输入
            var phase = p.Phase;
            var timeline = session.Timeline;
            if (!string.IsNullOrWhiteSpace(phase) && timeline != null)
            {
                var last = timeline.Count > 0 ? timeline[^1] : null;
                var lastPhase = last?.Phase;
                if (!string.Equals(lastPhase, phase, StringComparison.Ordinal))
                {
                    timeline.Add(new TimelineEntry(phase, p.Message ?? "", DateTimeOffset.UtcNow));
                }
            }

            // ─────────────────────────────────────────────
            //  可读内容抽取（避免把 token 流直接当日志刷屏）
            // ─────────────────────────────────────────────
            var stepStatus = p.StepStatus ?? "";
            var isCompleted = stepStatus.Contains("Completed", StringComparison.OrdinalIgnoreCase);
            var isFailed = stepStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase);
            var includeBody = isCompleted || isFailed || string.Equals(p.StepType, "vote", StringComparison.OrdinalIgnoreCase);
            var errorText = isFailed ? (p.Message ?? "Step failed") : null;
            
            // CRITICAL: Sync session.ExistingHypothesis to state.existing_hypothesis after ANY llm_call that outputs state
            // This ensures that if ExistingHypothesis is updated during execution, it gets propagated to state
            // We check for llm_call steps that might output state (init_state, update_state, or any step that modifies state)
            if (isCompleted && string.Equals(p.StepType, "llm_call", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.AssistantResponse) && !string.IsNullOrWhiteSpace(session.ExistingHypothesis))
            {
                try
                {
                    // Try to sync existing_hypothesis to state.json if the response contains state
                    SyncExistingHypothesisToStateIfNeeded(session, p.AssistantResponse);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AXIOM] Failed to sync existing_hypothesis after {StepId} (non-critical)", p.StepId);
                }
            }

            // Streaming optimization:
            // - During streaming, send prompts only once (first token) to avoid huge SSE payload per token.
            // - Use accumulated content from StreamingToken when available.
            var stream = p.StreamingToken;
            var assistant = stream?.AccumulatedContent ?? p.AssistantResponse;
            var assistantBody = includeBody ? TruncateHead(assistant, MaxBodyChars) : null;
            string? tokenDelta = null;

            string? sys;
            string? user;
            if (stream != null)
            {
                // Compute tokenDelta by diffing accumulated content (small SSE payload, smooth UI append).
                var stepId = p.StepId ?? stream.ProposalId;
                var key = $"{session.Id}:{stepId}";
                var curText = assistant ?? "";
                var curLen = curText.Length;
                var lastLen = StreamLastLen.GetOrAdd(key, 0);
                if (lastLen < 0 || lastLen > curLen) lastLen = 0;
                if (curLen > lastLen)
                {
                    tokenDelta = curText[lastLen..];
                    StreamLastLen[key] = curLen;
                }

                if (stream.IsLastToken)
                {
                    StreamLastLen.TryRemove(key, out _);
                }

                // Only send prompts once, to seed frontend history.
                if (stream.IsFirstToken)
                {
                    sys = stream.SystemPrompt ?? p.SystemPrompt;
                    user = stream.UserPrompt ?? p.UserPrompt;
                }
                else
                {
                    sys = null;
                    user = null;
                }
            }
            else
            {
                sys = p.SystemPrompt;  // no truncation
                user = p.UserPrompt;   // no truncation
            }

            // 推送 SSE 事件（类似 PaperReview 的“可读事件”，而不是 token dump）
            session.EventHub.Publish(new ProgressEvent
            {
                SessionId = session.Id,
                Phase = p.Phase,
                Message = p.Message,
                ProgressPercent = percent,

                WorkerId = stream?.WorkerId ?? p.TaskId,
                Depth = p.Depth,
                StepId = p.StepId,
                StepType = p.StepType,
                StepStatus = p.StepStatus,

                SystemPrompt = sys,
                UserPrompt = user,
                // NOTE: streaming 期间不推送 preview（会导致“大 payload + 替换全文”，反而更卡）
                AssistantResponsePreview = includeBody ? TruncateHead(assistant, 1600) : null,
                AssistantResponse = assistantBody,
                Error = errorText,

                ProviderName = stream?.ProviderName,
                TokenIndex = stream?.TokenIndex,
                TokenDelta = includeBody ? null : tokenDelta,

                VoteRound = p.VoteRound ?? 0,
                VoteMaxRounds = p.VoteMaxRounds ?? 0,
                VoteK = p.VoteK ?? 0,
                VoteCurrentVotes = p.VoteCurrentVotes ?? 0,

                ParallelTotal = p.ParallelTotal ?? 0,
                ParallelCompleted = p.ParallelCompleted ?? 0,
                ParallelFailed = p.ParallelFailed ?? 0,

                TotalLlmCalls = session.TotalLlmCalls,
                TotalTokens = session.TotalTokens
            });

            // Graph snapshot (for theorem loop): parse full update_state JSON and emit a small graph payload.
            if (isCompleted && TryExtractGraph(p.StepId, p.StepType, p.AssistantResponse, out var graph))
            {
                // Persist into graph store (best-effort; NEVER throw from this boundary).
                _ = PersistGraphBestEffortAsync(session.Id, graph);
                session.EventHub.Publish(graph with { SessionId = session.Id });
            }


            _logger.LogDebug("[AXIOM] {Session} {Phase} {Step} {Status}",
                session.Id, p.Phase, p.StepId, p.StepStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AXIOM] HandleProgress failed (session={SessionId}, phase={Phase}, step={StepId}, type={StepType})",
                session.Id, p.Phase, p.StepId, p.StepType);
        }
    }

    private async Task PersistGraphBestEffortAsync(string sessionId, GraphEvent graph)
    {
        try
        {
            await _graphStore.UpsertFromGraphEventAsync(sessionId, graph);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AXIOM] GraphStore upsert failed (session={SessionId})", sessionId);
        }
    }

    // ============================================================
    //  同步 session.ExistingHypothesis 到 state.existing_hypothesis (从 state.json 文件)
    // ============================================================
    private void SyncExistingHypothesisToStateIfNeeded(AxiomSession session, string assistantResponse)
    {
        try
        {
            // First, try to update state.json directly if it exists
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", session.Id);
            var stateJsonPath = Path.Combine(outputDir, "artifacts", "state.json");
            
            if (File.Exists(stateJsonPath))
            {
                try
                {
                    var stateJson = File.ReadAllText(stateJsonPath);
                    using var doc = JsonDocument.Parse(stateJson);
                    var root = doc.RootElement.Clone();
                    var stateObj = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetRawText());
                    
                    if (stateObj != null)
                    {
                        var currentExistingHyp = stateObj.ContainsKey("existing_hypothesis") 
                            ? stateObj["existing_hypothesis"]?.ToString() ?? "" 
                            : "";
                        var sessionExistingHyp = session.ExistingHypothesis?.Trim() ?? "";
                        
                        // If session has a value and state doesn't, or if they differ, update state
                        if (!string.IsNullOrWhiteSpace(sessionExistingHyp) && 
                            (string.IsNullOrWhiteSpace(currentExistingHyp) || 
                             !currentExistingHyp.Equals(sessionExistingHyp, StringComparison.Ordinal)))
                        {
                            stateObj["existing_hypothesis"] = sessionExistingHyp;
                            
                            var syncJsonOptions = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            };
                            var updatedJson = JsonSerializer.Serialize(stateObj, syncJsonOptions);
                            
                            File.WriteAllText(stateJsonPath, updatedJson);
                            _logger.LogInformation("[AXIOM] ✅ Synced session.ExistingHypothesis ({Length} chars) to state.existing_hypothesis in state.json", 
                                session.Id, sessionExistingHyp.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AXIOM] Failed to sync existing_hypothesis from state.json (non-critical)", session.Id);
                }
            }
            
            // Also try to parse from assistantResponse if it contains state
            SyncExistingHypothesisFromResponse(session, assistantResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AXIOM] Failed to sync existing_hypothesis (non-critical)", session.Id);
        }
    }

    // ============================================================
    //  从 LLM 响应中同步 session.ExistingHypothesis 到 state.existing_hypothesis
    // ============================================================
    private void SyncExistingHypothesisFromResponse(AxiomSession session, string assistantResponse)
    {
        try
        {
            // Try to parse the assistant response as JSON (init_state output)
            JsonElement root;
            var trimmed = assistantResponse.Trim();
            if (trimmed.Length == 0)
                return;

            // Try parsing directly
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                root = doc.RootElement.Clone();
            }
            catch
            {
                // Try stripping markdown code fences
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    var firstNl = trimmed.IndexOf('\n');
                    if (firstNl >= 0 && firstNl + 1 < trimmed.Length)
                    {
                        var inner = trimmed[(firstNl + 1)..];
                        var endFence = inner.LastIndexOf("```", StringComparison.Ordinal);
                        if (endFence >= 0)
                        {
                            var body = inner[..endFence].Trim();
                            try
                            {
                                using var doc = JsonDocument.Parse(body);
                                root = doc.RootElement.Clone();
                            }
                            catch
                            {
                                return; // Failed to parse
                            }
                        }
                        else
                        {
                            return; // Failed to parse
                        }
                    }
                    else
                    {
                        return; // Failed to parse
                    }
                }
                else
                {
                    // Try first {...} block
                    var firstBrace = trimmed.IndexOf('{');
                    var lastBrace = trimmed.LastIndexOf('}');
                    if (firstBrace >= 0 && lastBrace > firstBrace)
                    {
                        var obj = trimmed[firstBrace..(lastBrace + 1)].Trim();
                        try
                        {
                            using var doc = JsonDocument.Parse(obj);
                            root = doc.RootElement.Clone();
                        }
                        catch
                        {
                            return; // Failed to parse
                        }
                    }
                    else
                    {
                        return; // Failed to parse
                    }
                }
            }

            // Some workflows may wrap output as { "state": { ... } }.
            if (root.TryGetProperty("state", out var wrappedState) && wrappedState.ValueKind == JsonValueKind.Object)
            {
                root = wrappedState;
            }

            // Check if existing_hypothesis exists and differs from session.ExistingHypothesis
            var stateExistingHyp = "";
            if (root.TryGetProperty("existing_hypothesis", out var existingHypProp) && 
                existingHypProp.ValueKind == JsonValueKind.String)
            {
                stateExistingHyp = existingHypProp.GetString() ?? "";
            }

            var sessionExistingHyp = session.ExistingHypothesis?.Trim() ?? "";
            
            // If session has a value and state doesn't, or if they differ, update state
            if (!string.IsNullOrWhiteSpace(sessionExistingHyp) && 
                (string.IsNullOrWhiteSpace(stateExistingHyp) || 
                 !stateExistingHyp.Equals(sessionExistingHyp, StringComparison.Ordinal)))
            {
                // Update the JSON object
                var stateObj = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetRawText());
                if (stateObj != null)
                {
                    stateObj["existing_hypothesis"] = sessionExistingHyp;
                    
                    var syncJsonOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var updatedJson = JsonSerializer.Serialize(stateObj, syncJsonOptions);
                    
                    // Save back to artifacts/state.json
                    var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", session.Id);
                    var stateJsonPath = Path.Combine(outputDir, "artifacts", "state.json");
                    if (File.Exists(stateJsonPath))
                    {
                        File.WriteAllText(stateJsonPath, updatedJson);
                        _logger.LogInformation("[AXIOM] Synced session.ExistingHypothesis to state.existing_hypothesis after init_state", session.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AXIOM] Failed to sync existing_hypothesis (non-critical)", session.Id);
        }
    }

    private static string? Truncate(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "\n…(truncated)…";
    }

    private static string? TruncateHead(string? s, int maxChars) => Truncate(s, maxChars);

    private static string? TruncateTail(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= maxChars) return s;
        return "…(truncated)…\n" + s[^maxChars..];
    }

    private static bool TryExtractGraph(string? stepId, string? stepType, string? assistantResponse, out GraphEvent graph)
    {
        graph = new GraphEvent();
        if (string.IsNullOrWhiteSpace(assistantResponse)) return false;
        // We can extract a DAG snapshot from:
        // - llm_call outputs that contain {axioms/theorems/...}
        // - checkpoint outputs that emit a JSON snapshot of state/theorems (token-free)
        if (!string.Equals(stepType, "llm_call", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(stepType, "checkpoint", StringComparison.OrdinalIgnoreCase))
            return false;

        // Graph snapshot policy:
        // - Historically we only extracted graph from "update_state" (axiom_theorem_loop).
        // - HPL/HPA workflows evolve `state` via deterministic transform/hpa, so the earliest full snapshot
        //   is usually `init_state` (or any llm_call that returns {axioms:[..], theorems:[..]} / {state:{..}}).
        // - Therefore: try best-effort extraction from ANY llm_call output that contains axioms/theorems.

        static IEnumerable<string> EnumerateJsonCandidates(string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) yield break;

            // 1) Raw text
            yield return trimmed;

            // 2) Strip markdown code fences: ```json ... ```
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNl = trimmed.IndexOf('\n');
                if (firstNl >= 0 && firstNl + 1 < trimmed.Length)
                {
                    var inner = trimmed[(firstNl + 1)..];
                    var endFence = inner.LastIndexOf("```", StringComparison.Ordinal);
                    if (endFence >= 0)
                    {
                        var body = inner[..endFence].Trim();
                        if (body.Length > 0) yield return body;
                    }
                }
            }

            // 3) Best-effort: take first {...} block (handles accidental pre/post text)
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                var obj = trimmed[firstBrace..(lastBrace + 1)].Trim();
                if (obj.Length > 0) yield return obj;
            }
        }

        static bool TryParseJsonObject(string raw, out JsonElement root)
        {
            foreach (var candidate in EnumerateJsonCandidates(raw))
            {
                try
                {
                    using var doc = JsonDocument.Parse(candidate);
                    root = doc.RootElement.Clone(); // detach from doc lifetime
                    return root.ValueKind == JsonValueKind.Object;
                }
                catch
                {
                    // ignore and try next candidate
                }
            }
            root = default;
            return false;
        }

        try
        {
            if (!TryParseJsonObject(assistantResponse, out var root))
                return false;

            // Some workflows may wrap output as { "state": { ... } }.
            if (root.TryGetProperty("state", out var wrappedState) && wrappedState.ValueKind == JsonValueKind.Object)
            {
                root = wrappedState;
            }

            var axioms = new List<string>();
            if (root.TryGetProperty("axioms", out var ax) && ax.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in ax.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.String) axioms.Add(a.GetString() ?? "");
                }
            }

            // Optional: assumptions (e.g. S1) are extra premises that are not axioms.
            // We include them in graph snapshot so the DAG can render them as Assumption nodes.
            var assumptions = new List<AssumptionNode>();
            if (root.TryGetProperty("assumptions", out var asm) && asm.ValueKind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var a in asm.EnumerateArray())
                {
                    idx++;
                    if (a.ValueKind == JsonValueKind.Object)
                    {
                        var id = a.TryGetProperty("id", out var aid) && aid.ValueKind == JsonValueKind.String ? aid.GetString() ?? "" : "";
                        var stmt = a.TryGetProperty("statement", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
                        var mot = a.TryGetProperty("motivation", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(id)) id = $"S{idx}";
                        assumptions.Add(new AssumptionNode { Id = id, Statement = stmt, Motivation = mot });
                    }
                    else if (a.ValueKind == JsonValueKind.String)
                    {
                        assumptions.Add(new AssumptionNode { Id = $"S{idx}", Statement = a.GetString() ?? "", Motivation = "" });
                    }
                }
            }

            var theorems = new List<TheoremNode>();
            if (root.TryGetProperty("theorems", out var th) && th.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in th.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var id = t.TryGetProperty("id", out var tid) && tid.ValueKind == JsonValueKind.String ? tid.GetString() ?? "" : "";
                    var stmt = t.TryGetProperty("statement", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
                    var proof = t.TryGetProperty("proof", out var pf) && pf.ValueKind == JsonValueKind.String ? pf.GetString() ?? "" : "";
                    var deps = new List<string>();
                    if (t.TryGetProperty("depends_on", out var dp) && dp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dp.EnumerateArray())
                        {
                            if (d.ValueKind == JsonValueKind.String) deps.Add(d.GetString() ?? "");
                        }
                    }
                    else if (t.TryGetProperty("dependsOn", out var dp2) && dp2.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dp2.EnumerateArray())
                        {
                            if (d.ValueKind == JsonValueKind.String) deps.Add(d.GetString() ?? "");
                        }
                    }
                    theorems.Add(new TheoremNode { Id = id, Statement = stmt, Proof = proof, DependsOn = deps });
                }
            }

            var iteration = root.TryGetProperty("iteration", out var it) && it.ValueKind == JsonValueKind.Number
                ? it.GetInt32()
                : theorems.Count;

            graph = new GraphEvent
            {
                Iteration = iteration,
                Axioms = axioms,
                Assumptions = assumptions,
                Theorems = theorems
            };

            return axioms.Count > 0 || assumptions.Count > 0 || theorems.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}