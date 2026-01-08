using System.Diagnostics;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.AgenticRag;

// ============================================================
//  AgenticRagGAgent (Framework-level)
//
//  中文 + ASCII:
//  - 单 Agent 内部扮演四个角色：Planner/Retriever/Synthesizer/Critic（可替换策略）。
//  - Loop 由框架掌控：有界轮次/预算/停止条件，避免无限循环与输出爆炸。
//  - 默认不附加 tools（防止递归 tool-loop 与副作用扩散）。
// ============================================================

public abstract class AgenticRagGAgent : AIGAgentBase<AgenticRagState, AgenticRagConfig>
{
    private IAgenticRagRetriever? _retriever;
    private IAgenticRagPlanner? _planner;
    private IAgenticRagSynthesizer? _synthesizer;
    private IAgenticRagCritic? _critic;

    /// <summary>
    /// Optional trace store (injected by ExecutionTraceStoreInjector when property exists and DI is configured).
    /// Used in task 6 (ExecutionTrace integration).
    /// </summary>
    protected IExecutionTraceStore? ExecutionTraceStore { get; set; }

    public override Task<string> GetDescriptionAsync()
    {
        var s = GetCustomState();
        var last = string.IsNullOrWhiteSpace(s.LastRunId) ? "none" : s.LastRunId;
        return Task.FromResult($"AgenticRagGAgent: last_run={last}, last_stop={s.LastStopReason}");
    }

    protected override void ConfigCustom(AgenticRagConfig customConfig)
    {
        // Safe defaults (conservative) when not configured.
        // NOTE: proto int fields default to 0, so treat <=0 as "unset".
        if (customConfig.MaxIterations <= 0) customConfig.MaxIterations = 3;
        if (customConfig.MaxEvidenceItems <= 0) customConfig.MaxEvidenceItems = 8;
        if (customConfig.MaxEvidenceChars <= 0) customConfig.MaxEvidenceChars = 800;
        if (customConfig.MaxContextChars <= 0) customConfig.MaxContextChars = 8000;
        if (customConfig.CallTimeoutMs <= 0) customConfig.CallTimeoutMs = 30_000;
        // enable_execution_trace default is false in proto; keep it opt-in by config.
    }

    // ------------------------------------------------------------
    //  Strategy factories (override in derived agents)
    // ------------------------------------------------------------

    protected virtual IAgenticRagRetriever CreateRetriever()
        => new MemoryStoreAgenticRagRetriever(MemoryStore, MemoryVectorIndex);

    protected virtual IAgenticRagPlanner CreatePlanner()
        => new DefaultPlanner();

    protected virtual IAgenticRagSynthesizer CreateSynthesizer()
        => new DefaultSynthesizer();

    protected virtual IAgenticRagCritic CreateCritic()
        => new DefaultCritic();

    protected IAgenticRagRetriever Retriever => _retriever ??= CreateRetriever();
    protected IAgenticRagPlanner Planner => _planner ??= CreatePlanner();
    protected IAgenticRagSynthesizer Synthesizer => _synthesizer ??= CreateSynthesizer();
    protected IAgenticRagCritic Critic => _critic ??= CreateCritic();

    // ------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------

    public virtual async Task<AgenticRagResponse> AnswerAsync(
        AgenticRagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        if (request == null) throw new ArgumentNullException(nameof(request));

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId.Trim();

        var runId = Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTime.UtcNow;

        var stopwatch = Stopwatch.StartNew();
        using var activity = WorkflowTelemetry.StartWorkflow(runId, workflowName: "agentic_rag", coordinatorId: GetCoordinatorGuid());

        var cfg = GetCustomConfig();
        var budget = ResolveBudget(cfg, request.Budget);
        var traceStore = ExecutionTraceStore;
        var traceIterations = cfg.EnableExecutionTrace && traceStore != null
            ? new List<AgenticRagIterationTrace>()
            : null;

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["request_id"] = requestId,
            ["run_id"] = runId,
            ["agent_id"] = Id.ToString(),
            ["planner"] = Planner.Name,
            ["retriever"] = Retriever.Name,
            ["synthesizer"] = Synthesizer.Name,
            ["critic"] = Critic.Name,
            ["max_iterations"] = budget.MaxIterations.ToString(),
            ["max_evidence_items"] = budget.MaxEvidenceItems.ToString()
        };

        var evidence = new List<RagEvidenceSummary>(capacity: Math.Clamp(budget.MaxEvidenceItems, 1, 64));
        var answer = string.Empty;
        var stopReason = RagStopReason.Unspecified;
        var iterations = 0;
        var iterationsExecuted = 0;

        try
        {
            for (iterations = 0; iterations < budget.MaxIterations; iterations++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                iterationsExecuted++;

                var iterTrace = traceIterations != null ? new AgenticRagIterationTrace { Iteration = iterations } : null;
                if (iterTrace != null)
                    traceIterations!.Add(iterTrace);

                // ----------------------------
                // Plan
                // ----------------------------
                RagPlan plan;
                using (var step = WorkflowTelemetry.StartStep(runId, stepId: $"plan-{iterations}", stepType: "plan", depth: 1))
                {
                    try
                    {
                        plan = await Planner.PlanAsync(request, iterations, evidence, cancellationToken);
                        iterTrace?.PlanShouldStop = plan.ShouldStop;
                        WorkflowTelemetry.RecordStepCompleted(step, stepId: $"plan-{iterations}", stepType: "plan",
                            durationMs: 0);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (iterTrace != null)
                        {
                            iterTrace.ErrorStage = "plan";
                            iterTrace.ErrorMessage = ex.Message;
                        }
                        diagnostics["plan_error"] = ex.GetType().Name;
                        diagnostics["plan_error_message"] = ex.Message;
                        WorkflowTelemetry.RecordStepFailed(step, stepId: $"plan-{iterations}", stepType: "plan",
                            durationMs: 0, error: ex.Message);
                        stopReason = RagStopReason.Failed;
                        break;
                    }
                }

                if (plan.ShouldStop && evidence.Count > 0)
                {
                    stopReason = RagStopReason.Succeeded;
                    break;
                }

                var retrievals = plan.Retrievals;
                if (retrievals.Count == 0)
                {
                    retrievals = new[]
                    {
                        new RagRetrieveRequest
                        {
                            RequestId = requestId,
                            Query = request.Query,
                            MaxResults = budget.MaxEvidenceItems,
                            MaxSnippetChars = budget.MaxEvidenceChars,
                            Scope = request.Scope ?? BuildDefaultScope(cfg),
                            MemoryId = request.MemoryId ?? cfg.MemoryId
                        }
                    };
                }

                iterTrace?.RetrievalCount = retrievals.Count;

                // ----------------------------
                // Retrieve (best-effort)
                // ----------------------------
                evidence.Clear();
                using (var step = WorkflowTelemetry.StartStep(runId, stepId: $"retrieve-{iterations}", stepType: "retrieve", depth: 1))
                {
                    try
                    {
                        foreach (var r in retrievals)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var prepared = await PrepareRetrieveRequestAsync(r, cfg, budget, cancellationToken);
                            var items = await Retriever.RetrieveAsync(prepared, cancellationToken);
                            foreach (var it in items)
                            {
                                evidence.Add(it);
                                if (evidence.Count >= budget.MaxEvidenceItems)
                                    break;
                            }

                            if (evidence.Count >= budget.MaxEvidenceItems)
                                break;
                        }

                        DedupEvidenceInPlace(evidence, budget.MaxEvidenceItems);
                        iterTrace?.EvidenceCount = evidence.Count;
                        WorkflowTelemetry.RecordStepCompleted(step, stepId: $"retrieve-{iterations}", stepType: "retrieve",
                            durationMs: 0);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (iterTrace != null)
                        {
                            iterTrace.ErrorStage = "retrieve";
                            iterTrace.ErrorMessage = ex.Message;
                        }
                        diagnostics["retrieve_error"] = ex.GetType().Name;
                        diagnostics["retrieve_error_message"] = ex.Message;
                        WorkflowTelemetry.RecordStepFailed(step, stepId: $"retrieve-{iterations}", stepType: "retrieve",
                            durationMs: 0, error: ex.Message);
                        // Best-effort: treat as no evidence.
                        evidence.Clear();
                    }
                }

                if (evidence.Count == 0)
                {
                    // If no evidence and no more iterations left, stop.
                    if (iterations + 1 >= budget.MaxIterations)
                    {
                        stopReason = RagStopReason.NoEvidence;
                        break;
                    }

                    // Otherwise let the loop continue (planner may adjust query).
                }

                // ----------------------------
                // Synthesize
                // ----------------------------
                RagSynthesisResult draft;
                using (var step = WorkflowTelemetry.StartStep(runId, stepId: $"synthesize-{iterations}", stepType: "synthesize", depth: 1))
                {
                    try
                    {
                        draft = await Synthesizer.SynthesizeAsync(request, evidence, cancellationToken);
                        iterTrace?.DraftChars = draft.Draft?.Length ?? 0;
                        WorkflowTelemetry.RecordStepCompleted(step, stepId: $"synthesize-{iterations}", stepType: "synthesize",
                            durationMs: 0);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (iterTrace != null)
                        {
                            iterTrace.ErrorStage = "synthesize";
                            iterTrace.ErrorMessage = ex.Message;
                        }
                        diagnostics["synthesize_error"] = ex.GetType().Name;
                        diagnostics["synthesize_error_message"] = ex.Message;
                        WorkflowTelemetry.RecordStepFailed(step, stepId: $"synthesize-{iterations}", stepType: "synthesize",
                            durationMs: 0, error: ex.Message);
                        stopReason = RagStopReason.Failed;
                        break;
                    }
                }

                if (draft.Diagnostics.Count > 0)
                {
                    foreach (var (k, v) in draft.Diagnostics)
                        diagnostics[$"synth.{k}"] = v;
                }

                // ----------------------------
                // Critique
                // ----------------------------
                RagCritiqueResult critique;
                using (var step = WorkflowTelemetry.StartStep(runId, stepId: $"critique-{iterations}", stepType: "critique", depth: 1))
                {
                    try
                    {
                        critique = await Critic.CritiqueAsync(request, draft, evidence, cancellationToken);
                        iterTrace?.CriticPassed = critique.Passed;
                        iterTrace?.CriticGaps = critique.Gaps.Count;
                        WorkflowTelemetry.RecordStepCompleted(step, stepId: $"critique-{iterations}", stepType: "critique",
                            durationMs: 0);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (iterTrace != null)
                        {
                            iterTrace.ErrorStage = "critique";
                            iterTrace.ErrorMessage = ex.Message;
                        }
                        diagnostics["critique_error"] = ex.GetType().Name;
                        diagnostics["critique_error_message"] = ex.Message;
                        WorkflowTelemetry.RecordStepFailed(step, stepId: $"critique-{iterations}", stepType: "critique",
                            durationMs: 0, error: ex.Message);
                        stopReason = RagStopReason.Failed;
                        break;
                    }
                }

                if (critique.Diagnostics.Count > 0)
                {
                    foreach (var (k, v) in critique.Diagnostics)
                        diagnostics[$"critic.{k}"] = v;
                }

                if (critique.Passed)
                {
                    answer = draft.Draft ?? string.Empty;
                    stopReason = RagStopReason.Succeeded;
                    break;
                }

                // Bounce-back: keep the latest draft (best-effort), but continue if budget allows.
                answer = draft.Draft ?? string.Empty;
                diagnostics["critic_passed"] = "false";
                diagnostics["critic_gaps"] = critique.Gaps.Count.ToString();

                if (iterations + 1 >= budget.MaxIterations)
                {
                    stopReason = RagStopReason.BudgetExceeded;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = RagStopReason.Cancelled;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Record workflow completion best-effort.
            if (stopReason == RagStopReason.Failed)
            {
                WorkflowTelemetry.RecordWorkflowFailed(activity, "agentic_rag", stopwatch.Elapsed.TotalMilliseconds,
                    diagnostics.TryGetValue("error", out var err) ? err : "failed");
            }
            else
            {
                WorkflowTelemetry.RecordWorkflowCompleted(activity, "agentic_rag", stopwatch.Elapsed.TotalMilliseconds,
                    totalSteps: Math.Max(0, iterationsExecuted),
                    totalTokens: 0,
                    totalLLMCalls: 0,
                    maxDepth: 1);
            }
        }

        if (stopReason == RagStopReason.Unspecified)
        {
            stopReason = evidence.Count == 0 ? RagStopReason.NoEvidence : RagStopReason.BudgetExceeded;
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = stopReason switch
            {
                RagStopReason.NoEvidence => "No supporting evidence was found for this query.",
                RagStopReason.BudgetExceeded => "Reached budget limits before producing a fully supported answer.",
                RagStopReason.Failed => "Failed to generate an answer due to an internal error.",
                _ => string.Empty
            };
        }

        diagnostics["stop_reason"] = stopReason.ToString();
        diagnostics["iterations"] = Math.Max(0, iterationsExecuted).ToString();
        diagnostics["evidence_items"] = evidence.Count.ToString();
        diagnostics["duration_ms"] = stopwatch.ElapsedMilliseconds.ToString();

        // Optional: export bounded ExecutionTrace (best-effort).
        if (cfg.EnableExecutionTrace && traceStore != null && traceIterations != null)
        {
            var endedAtUtc = DateTime.UtcNow;
            var exported = await AgenticRagExecutionTraceExporter.TryExportAsync(
                Logger,
                traceStore,
                requestId,
                runId,
                agentId: Id,
                startedAtUtc,
                endedAtUtc,
                stopwatch.ElapsedMilliseconds,
                stopReason,
                iterationsExecuted,
                evidence,
                answer,
                diagnostics,
                traceIterations);

            diagnostics[exported ? "execution_trace_id" : "execution_trace_export_failed"] =
                exported ? runId : "true";
        }

        // Publish cross-boundary answer event (Protobuf).
        var evt = new AgenticRagAnswerEvent
        {
            RequestId = requestId,
            RunId = runId,
            Answer = answer,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        evt.Evidence.AddRange(evidence);
        foreach (var (k, v) in diagnostics)
        {
            // Keep bounded/strings only; caller should avoid secrets.
            evt.Diagnostics[k] = v ?? string.Empty;
        }

        await PublishAsync(evt, ct: cancellationToken);

        // Best-effort: update custom state summary (skip if event sourcing is active).
        TryUpdateCustomStateBestEffort(runId, iterationsExecuted, stopReason, evidence);

        return new AgenticRagResponse
        {
            RequestId = requestId,
            RunId = runId,
            Answer = answer,
            Evidence = evidence.ToList(),
            StopReason = stopReason,
            Iterations = Math.Max(0, iterationsExecuted),
            Diagnostics = diagnostics
        };
    }

    // ------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------

    private static RagBudget ResolveBudget(AgenticRagConfig cfg, RagBudget? overrideBudget)
    {
        var maxIterations = overrideBudget is { MaxIterations: > 0 } o1 ? o1.MaxIterations : cfg.MaxIterations;
        var maxEvidenceItems = overrideBudget is { MaxEvidenceItems: > 0 } o2 ? o2.MaxEvidenceItems : cfg.MaxEvidenceItems;
        var maxEvidenceChars = overrideBudget is { MaxEvidenceChars: > 0 } o3 ? o3.MaxEvidenceChars : cfg.MaxEvidenceChars;
        var maxContextChars = overrideBudget is { MaxContextChars: > 0 } o4 ? o4.MaxContextChars : cfg.MaxContextChars;

        var callTimeout = overrideBudget?.CallTimeout ??
                          (cfg.CallTimeoutMs > 0 ? TimeSpan.FromMilliseconds(cfg.CallTimeoutMs) : (TimeSpan?)null);

        return new RagBudget
        {
            MaxIterations = Math.Clamp(maxIterations, 1, 16),
            MaxEvidenceItems = Math.Clamp(maxEvidenceItems, 1, 200),
            MaxEvidenceChars = Math.Clamp(maxEvidenceChars, 0, 4000),
            MaxContextChars = Math.Clamp(maxContextChars, 0, 200_000),
            CallTimeout = callTimeout
        };
    }

    private static MemoryScope? BuildDefaultScope(AgenticRagConfig cfg)
    {
        if (cfg.ScopeType == MemoryScopeType.Unspecified)
            return null;

        if (string.IsNullOrWhiteSpace(cfg.ScopeId))
            return null;

        return new MemoryScope
        {
            Type = cfg.ScopeType,
            ScopeId = cfg.ScopeId.Trim()
        };
    }

    private async Task<RagRetrieveRequest> PrepareRetrieveRequestAsync(
        RagRetrieveRequest request,
        AgenticRagConfig cfg,
        RagBudget budget,
        CancellationToken cancellationToken)
    {
        var r = request with
        {
            MaxResults = request.MaxResults > 0 ? request.MaxResults : budget.MaxEvidenceItems,
            MaxSnippetChars = request.MaxSnippetChars > 0 ? request.MaxSnippetChars : budget.MaxEvidenceChars,
            Scope = request.Scope ?? BuildDefaultScope(cfg),
            MemoryId = string.IsNullOrWhiteSpace(request.MemoryId) ? cfg.MemoryId : request.MemoryId
        };

        // Best-effort: compute query embedding when possible and not provided.
        if (r.QueryEmbedding == null && MemoryVectorIndex != null)
        {
            try
            {
                if (TryGetEmbeddingGenerator(out _))
                {
                    var embedding = await GenerateEmbeddingAsync(r.Query, cancellationToken: cancellationToken);
                    if (embedding is { Vector.Length: > 0 })
                    {
                        r = r with { QueryEmbedding = embedding.Vector.ToArray() };
                    }
                }
            }
            catch
            {
                // best-effort: keep lexical-only
            }
        }

        return r;
    }

    private void TryUpdateCustomStateBestEffort(
        string runId,
        int iterations,
        RagStopReason stopReason,
        IReadOnlyList<RagEvidenceSummary> evidence)
    {
        try
        {
            // AIGAgentBase<TCustomState> forbids direct state assignment when Event Sourcing is active.
            // Best-effort: skip if version > 0.
            if (GetCurrentVersion() > 0)
                return;

            var s = GetCustomState();
            s.LastRunId = runId;
            s.LastIterations = Math.Max(0, iterations);
            s.LastStopReason = stopReason.ToString();
            s.LastEvidence.Clear();
            s.LastEvidence.AddRange(evidence);
            CustomState = s;
        }
        catch
        {
            // best-effort
        }
    }

    private static void DedupEvidenceInPlace(List<RagEvidenceSummary> evidence, int maxItems)
    {
        if (evidence.Count <= 1)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var write = 0;

        for (var i = 0; i < evidence.Count; i++)
        {
            var e = evidence[i];
            var key = BuildEvidenceKey(e);
            if (!seen.Add(key))
                continue;

            evidence[write++] = e;
            if (write >= maxItems)
                break;
        }

        if (write < evidence.Count)
            evidence.RemoveRange(write, evidence.Count - write);
    }

    private static string BuildEvidenceKey(RagEvidenceSummary e)
    {
        if (e?.Citation?.RefCase == RagCitation.RefOneofCase.MemoryEntry &&
            e.Citation.MemoryEntry != null)
        {
            var m = e.Citation.MemoryEntry;
            return $"mem::{m.MemoryId}::{m.EntryId}";
        }

        if (e?.Citation?.RefCase == RagCitation.RefOneofCase.Uri &&
            e.Citation.Uri != null)
        {
            var u = e.Citation.Uri;
            return $"uri::{u.Uri}::{u.Start}-{u.End}";
        }

        return e?.EvidenceId ?? Guid.NewGuid().ToString("N");
    }

    private Guid GetCoordinatorGuid()
    {
        // Best-effort: prefer the raw-id part ("Type:RawId") if present, otherwise try parsing Id directly.
        if (AgentId.TrySplit(Id, out _, out var raw) && Guid.TryParse(raw, out var g))
            return g;

        return Guid.TryParse(Id, out var g2) ? g2 : Guid.Empty;
    }

    // ExecutionTrace export helpers live in `AgenticRagExecutionTraceExporter.cs`.

    // ------------------------------------------------------------
    //  Default strategies (MVP, deterministic; users can override)
    // ------------------------------------------------------------

    private sealed class DefaultPlanner : IAgenticRagPlanner
    {
        public string Name => "default";

        public Task<RagPlan> PlanAsync(
            AgenticRagRequest request,
            int iteration,
            IReadOnlyList<RagEvidenceSummary> evidenceSoFar,
            CancellationToken cancellationToken)
        {
            // MVP: always retrieve with the original query; stop early only if we already have evidence.
            var shouldStop = iteration > 0 && evidenceSoFar.Count > 0;
            return Task.FromResult(new RagPlan { ShouldStop = shouldStop });
        }
    }

    private sealed class DefaultSynthesizer : IAgenticRagSynthesizer
    {
        public string Name => "default";

        public Task<RagSynthesisResult> SynthesizeAsync(
            AgenticRagRequest request,
            IReadOnlyList<RagEvidenceSummary> evidence,
            CancellationToken cancellationToken)
        {
            if (evidence.Count == 0)
            {
                return Task.FromResult(new RagSynthesisResult
                {
                    Draft = "No supporting evidence was found for this query.",
                    UsedEvidence = Array.Empty<RagEvidenceSummary>()
                });
            }

            // MVP: deterministic draft that still includes evidence snippets (no hallucinations).
            var lines = new List<string>(capacity: Math.Min(8, evidence.Count) + 2)
            {
                "Evidence-based draft (MVP):"
            };

            foreach (var e in evidence.Take(8))
            {
                var s = (e.Snippet ?? string.Empty).Replace("\r", "").Trim();
                if (s.Length == 0) continue;
                lines.Add($"- {s}");
            }

            return Task.FromResult(new RagSynthesisResult
            {
                Draft = string.Join("\n", lines),
                UsedEvidence = evidence.Take(8).ToList()
            });
        }
    }

    private sealed class DefaultCritic : IAgenticRagCritic
    {
        public string Name => "default";

        public Task<RagCritiqueResult> CritiqueAsync(
            AgenticRagRequest request,
            RagSynthesisResult draft,
            IReadOnlyList<RagEvidenceSummary> evidence,
            CancellationToken cancellationToken)
        {
            // MVP: pass if we have at least one evidence item.
            if (evidence.Count > 0)
                return Task.FromResult(new RagCritiqueResult { Passed = true });

            return Task.FromResult(new RagCritiqueResult
            {
                Passed = false,
                Gaps = new[]
                {
                    new RagGap { Description = "No evidence collected." }
                },
                Feedback = "Need at least one evidence item to answer."
            });
        }
    }
}


