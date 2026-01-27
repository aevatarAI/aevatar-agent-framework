using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Aevatar.Agents.AGUI;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe.Pivot;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    /// <summary>
    /// Record for storing agent prompt information (system prompt, user prompt, materials context, and output).
    /// </summary>
    public sealed record AgentPromptRecord(
        string AgentName,
        string SystemPrompt,
        string UserPrompt,
        string? MaterialsContext,
        string RawOutput,
        DateTimeOffset Timestamp
    );
    // ============================================================
    //  ExecuteOneRoundAsync helpers (split to keep main file small)
    // ============================================================

    internal sealed class VibeRoundContext
    {
        public VibeRoundContext(
            ResearchSession session,
            string runId,
            SessionInputInDto input,
            string question,
            MaterialsSnapshot materials,
            Action<string> emitAssistantDelta)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Question = question ?? throw new ArgumentNullException(nameof(question));
            Materials = materials ?? throw new ArgumentNullException(nameof(materials));
            EmitAssistantDelta = emitAssistantDelta ?? (_ => { });
        }

        public ResearchSession Session { get; }
        public string RunId { get; }
        public SessionInputInDto Input { get; }
        public string Question { get; }
        public MaterialsSnapshot Materials { get; }
        public Action<string> EmitAssistantDelta { get; }
    }

    private async Task RunPivotDetectionAsync(VibeRoundContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.pivot_detection" });
        try
        {
            var currentDirection = await GetCurrentDirectionAsync(session.Id, ct);
            var pivotIntent = await DetectDirectionChangeAsync(
                session,
                ctx.RunId,
                ctx.Question,
                currentDirection,
                ctx.EmitAssistantDelta,
                ct);

            var pivotEmitter = _pivot.FeedbackEmitter;
            var pivotId = (pivotIntent.PivotId ?? string.Empty).Trim();

            // Execute pivot if high-confidence direction change detected
            if (pivotIntent is { IsDirectionChange: true } &&
                pivotIntent.Confidence >= _pivot.Options.ConfidenceThreshold)
            {
                try
                {
                    if (pivotId.Length == 0)
                        pivotId = $"pivot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                    pivotIntent = pivotIntent with { PivotId = pivotId };

                    session.Events.Publish(pivotEmitter.CreateDetectedEvent(session.Id, pivotId, pivotIntent));

                    if (pivotIntent.NeedsClarification)
                    {
                        session.Events.Publish(pivotEmitter.CreateClarificationRequestEvent(
                            session.Id,
                            pivotId,
                            pivotIntent.NewTopic,
                            pivotIntent.Confidence,
                            oldTopic: currentDirection));
                    }

                    session.Events.Publish(pivotEmitter.CreateStartedEvent(
                        session.Id,
                        pivotId,
                        currentDirection,
                        pivotIntent.NewTopic));

                    _host.Logger.LogInformation(
                        "Enqueueing pivot for session {SessionId}: {OldDirection} -> {NewDirection}",
                        session.Id, currentDirection ?? "(none)", pivotIntent.NewTopic ?? "(new)");

                    // Use queue for serialization (FR-012) and coordinator for DAG + agent notification
                    var capturedDirection = currentDirection;
                    var queueResult = await _pivot.Queue.EnqueueAsync(
                        pivotIntent,
                        async cancellationToken =>
                        {
                            var coordResult = await _pivot.AgentCoordinator.CoordinatePivotAsync(
                                pivotIntent,
                                capturedDirection,
                                cancellationToken);
                            return coordResult.DagOperation!;
                        },
                        ct);

                    if (queueResult.QueueFull)
                    {
                        _host.Logger.LogWarning(
                            "Pivot queue full for session {SessionId}, request rejected",
                            session.Id);
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            "pivot_queue_full",
                            errorCode: "pivot_queue_full"));
                        ctx.EmitAssistantDelta("\n⏳ 研究方向更新队列已满，请稍后重试...\n\n");
                    }
                    else if (queueResult.ErrorMessage != null)
                    {
                        _host.Logger.LogError(
                            "Pivot failed for session {SessionId}: {Error}",
                            session.Id, queueResult.ErrorMessage);
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            queueResult.ErrorMessage,
                            errorCode: "pivot_failed"));
                        ctx.EmitAssistantDelta("\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                    }
                    else if (queueResult.Operation != null)
                    {
                        var pivotOp = queueResult.Operation;

                        _host.Logger.LogInformation(
                            "Pivot completed for session {SessionId}: cancelled={Cancelled}, preserved={Preserved}, queued={Queued}",
                            session.Id, pivotOp.CancelledNodeIds.Count, pivotOp.PreservedNodeIds.Count, queueResult.Queued);

                        // Emit completion + progress events
                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.CancellingPlans,
                            count: pivotOp.CancelledNodeIds.Count,
                            progress: 0.6));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.PreservingKnowledge,
                            count: pivotOp.PreservedNodeIds.Count,
                            progress: 0.8));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.NotifyingAgents,
                            progress: 0.9));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.UpdatingDag,
                            progress: 1.0));

                        session.Events.Publish(pivotEmitter.CreateCompletedEvent(
                            session.Id,
                            pivotOp.PivotId,
                            pivotOp));

                        // Keep legacy custom event for existing UI logic.
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = NowMs(),
                            Name = "aevatar.vibe.pivot_completed",
                            Value = new
                            {
                                sessionId = session.Id,
                                pivotId = pivotOp.PivotId,
                                status = pivotOp.Status.ToString(),
                                cancelledCount = pivotOp.CancelledNodeIds.Count,
                                preservedCount = pivotOp.PreservedNodeIds.Count,
                                durationMs = pivotOp.DurationMs,
                                wasQueued = queueResult.Queued
                            }
                        });

                        // Notify user of completion
                        var queuedNote = queueResult.Queued ? " (队列等待后执行)" : "";
                        ctx.EmitAssistantDelta($"\n✓ 研究方向已更新完成{queuedNote} (取消了 {pivotOp.CancelledNodeIds.Count} 个待执行计划，保留了 {pivotOp.PreservedNodeIds.Count} 个已完成成果)\n\n");
                    }
                }
                catch (Exception pivotEx)
                {
                    _host.Logger.LogError(pivotEx, "Pivot execution failed for session {SessionId}", session.Id);
                    if (pivotId.Length > 0)
                    {
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            pivotEx.Message,
                            errorCode: "pivot_exception"));
                    }
                    ctx.EmitAssistantDelta("\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                    // Don't block the round - pivot failure is non-fatal
                }
            }
            else if (pivotIntent is { IsDirectionChange: true })
            {
                if (pivotId.Length == 0)
                    pivotId = $"pivot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                pivotIntent = pivotIntent with { PivotId = pivotId };
                session.Events.Publish(pivotEmitter.CreateDetectedEvent(session.Id, pivotId, pivotIntent));

                if (pivotIntent.NeedsClarification)
                {
                    session.Events.Publish(pivotEmitter.CreateClarificationRequestEvent(
                        session.Id,
                        pivotId,
                        pivotIntent.NewTopic,
                        pivotIntent.Confidence,
                        oldTopic: currentDirection));
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: detection failure should not block the round
            _host.Logger.LogWarning(ex, "Direction change detection failed for session {SessionId}", session.Id);
        }
        finally
        {
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.pivot_detection" });
        }
    }

    private async Task<(PlanResult Plan, SraDagSnapshot DagSnapshot, AgentPromptRecord? PromptRecord)> RunPlanPhaseAsync(
        VibeRoundContext ctx,
        string dagId,
        SraDagSnapshot dagSnap,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? raProvider,
        CancellationToken ct)
    {
        var session = ctx.Session;
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.ra_plan"
        });

        var (plan, promptRecord) = await TryGetPlanAsync(
            session.Id,
            ctx.Input,
            ctx.Question,
            ctx.Materials,
            dagSnap,
            recentTrace,
            raProvider,
            ct);
        if (!string.IsNullOrWhiteSpace(plan.RawJson))
        {
            EmitSection(ctx.EmitAssistantDelta, "### Plan (research_assistant)\n");
            ctx.EmitAssistantDelta("```json\n");
            ctx.EmitAssistantDelta(plan.RawJson!.Trim() + "\n");
            ctx.EmitAssistantDelta("```\n\n");
        }

        // ------------------------------------------------------------
        // Step: Persist plan into DAG (as "plan" nodes)
        //
        // DISABLED: Round plan nodes are no longer created during execution.
        // Milestones are created once during brief generation and remain fixed.
        // Only knowledge nodes should be created during execution.
        //
        // 中文说明：
        // - 已禁用：执行过程中不再创建 round plan 节点
        // - Milestones 在 brief 阶段一次性创建，之后保持不变
        // - 执行过程中只创建 knowledge 节点
        // ------------------------------------------------------------
        // try
        // {
        //     var m = BuildPlanDagMutation(session.Id, ctx.RunId, ctx.Question, plan);
        //     if (m != null)
        //     {
        //         dagSnap = await _core.Dag.ApplyMutationAsync(dagId, m, ct);
        //         session.Events.Publish(new CustomEvent
        //         {
        //             Timestamp = NowMs(),
        //             Name = "aevatar.vibe.plan_dag_written",
        //             Value = new { sessionId = session.Id, dagId, runId = ctx.RunId, mutationId = m.MutationId }
        //         });
        //     }
        // }
        // catch
        // {
        //     // best-effort only
        // }

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.ra_plan"
        });

        return (plan, dagSnap, promptRecord);
    }

    private async Task<(Dictionary<string, string> Outputs, bool MeshUsed, Dictionary<string, AgentPromptRecord> PromptRecords)> RunWorkerPhaseAsync(
        VibeRoundContext ctx,
        string dagId,
        SraDagSnapshot dagSnap,
        PlanResult plan,
        Func<string, string?> resolveProvider,
        List<LibrarianAxiomCandidate> librarianAxioms,
        List<string> factsWritten,
        CancellationToken ct)
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var promptRecords = new Dictionary<string, AgentPromptRecord>(StringComparer.OrdinalIgnoreCase);

        var localDagSnap = dagSnap;
        var meshUsed = await TryRunMeshWorkerPhaseAsync(
            ctx,
            dagId,
            plan,
            resolveProvider,
            outputs,
            ct,
            onDagRefreshed: s => localDagSnap = s);

        if (!meshUsed)
        {
            await RunFallbackWorkerPhaseAsync(
                ctx,
                dagId,
                plan,
                resolveProvider,
                outputs,
                librarianAxioms,
                factsWritten,
                promptRecords,
                ct,
                onDagRefreshed: s => localDagSnap = s,
                getDagSnapshot: () => localDagSnap);
        }

        return (outputs, meshUsed, promptRecords);
    }

    private async Task<bool> TryRunMeshWorkerPhaseAsync(
        VibeRoundContext ctx,
        string dagId,
        PlanResult plan,
        Func<string, string?> resolveProvider,
        Dictionary<string, string> outputs,
        CancellationToken ct,
        Action<SraDagSnapshot> onDagRefreshed)
    {
        var meshOpt = _mesh.Options.Value;
        if (!meshOpt.Enabled)
            return false;

        var meshUsed = false;
        try
        {
            var raw = await TryLoadOrSeedMeshAsync(ctx.Session, ctx.RunId, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // Mesh enabled but mesh.{yaml|json} absent AND cannot seed → fall back silently (best-effort event only).
                ctx.Session.Events.Publish(new CustomEvent
                {
                    Timestamp = NowMs(),
                    Name = "aevatar.vibe.mesh_missing",
                    Value = new { sessionId = ctx.Session.Id, runId = ctx.RunId }
                });
                return false;
            }

            var compile = _mesh.Compiler.Compile(raw!);
            if (!compile.Ok || compile.Definition == null)
            {
                HandleMeshErrorsOrFallback(
                    ctx.Session,
                    ctx.EmitAssistantDelta,
                    kind: "mesh.compile_failed",
                    errors: compile.Errors,
                    options: meshOpt,
                    outputs: outputs,
                    out meshUsed);
                return meshUsed;
            }

            var planRes = _mesh.Planner.Plan(ctx.Session.Id, ctx.RunId, compile.Definition);
            if (!planRes.Ok || planRes.Plan == null)
            {
                HandleMeshErrorsOrFallback(
                    ctx.Session,
                    ctx.EmitAssistantDelta,
                    kind: "mesh.plan_failed",
                    errors: planRes.Errors,
                    options: meshOpt,
                    outputs: outputs,
                    out meshUsed);
                return meshUsed;
            }

            var meshProviderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var requiredRoles = planRes.Plan.Nodes
                .Select(n => (n.Type ?? string.Empty).Trim().ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Provider gate per role (same UX as non-mesh path).
            foreach (var role in requiredRoles)
            {
                var p = await EnsureProviderRunnableOrPauseAsync(
                    ctx.Session,
                    ctx.RunId,
                    agent: role,
                    stepName: $"vibe.mesh.{role}",
                    resolveProvider: () => resolveProvider(role),
                    emitAssistantDelta: ctx.EmitAssistantDelta,
                    ct: ct);

                if (!string.IsNullOrWhiteSpace(p))
                    meshProviderMap[role] = p!;
            }

            string? ResolveProviderForMesh(string role) =>
                meshProviderMap.TryGetValue(role, out var p) ? p : resolveProvider(role);

            // Keep DAG snapshot fresh at the beginning of mesh execution.
            var freshDag = await _core.Dag.LoadSnapshotAsync(dagId, ct);
            onDagRefreshed(freshDag);

            var run = await _mesh.Runner.ExecuteAsync(
                ctx.Session,
                ctx.Input,
                ctx.Materials,
                freshDag,
                planRes.Plan,
                ctx.Question,
                ResolveProviderForMesh,
                ct);

            // Map node outputs -> existing outputs dictionary (by role).
            // This preserves downstream expectations: outputs["planner"], outputs["dag_builder"], ...
            var nodeById = planRes.Plan.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var nodeId in planRes.Plan.TopoOrder)
            {
                if (!run.OutputsByNodeId.TryGetValue(nodeId, out var text))
                    continue;
                if (!nodeById.TryGetValue(nodeId, out var node))
                    continue;

                var role = (node.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (role.Length == 0) continue;

                // First occurrence wins (stable via topo order).
                if (!outputs.ContainsKey(role))
                    outputs[role] = text;
            }

            if (run.Errors.Count > 0)
                outputs["mesh_errors"] = Bound(string.Join("\n", run.Errors.Take(20)), 4000);

            outputs["mesh_used"] = "true";
            meshUsed = true;

            // Best-effort artifacts for debugging/replay.
            TryWriteMeshRunArtifacts(ctx.Session.Id, ctx.RunId, raw!, planRes.Plan, run);

            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            HandleMeshErrorsOrFallback(
                ctx.Session,
                ctx.EmitAssistantDelta,
                kind: "mesh.exception",
                errors: [new Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError("mesh.exception", ex.Message, null)],
                options: meshOpt,
                outputs: outputs,
                out meshUsed);
            return meshUsed;
        }
    }

    private async Task RunFallbackWorkerPhaseAsync(
        VibeRoundContext ctx,
        string dagId,
        PlanResult plan,
        Func<string, string?> resolveProvider,
        Dictionary<string, string> outputs,
        List<LibrarianAxiomCandidate> librarianAxioms,
        List<string> factsWritten,
        Dictionary<string, AgentPromptRecord> promptRecords,
        CancellationToken ct,
        Action<SraDagSnapshot> onDagRefreshed,
        Func<SraDagSnapshot> getDagSnapshot)
    {
        // Worker roster (fallback-first).
        var workers = plan.Workers?.Where(w => !string.IsNullOrWhiteSpace(w.Agent)).ToList()
                      ?? new List<PlanWorker>
                      {
                          new() { Agent = "planner", Task = "Produce an executable plan and unknowns" },
                          new() { Agent = "reasoner", Task = "Provide grounded reasoning with explicit hypotheses" },
                          new() { Agent = "verifier", Task = "Verify hypotheses using multi-stage verification" },
                          new() { Agent = "dag_builder", Task = "Build DAG nodes from worker outputs" }
                      };

        // Deterministic ordering (helps librarian->dag_builder handoff).
        workers = workers
            .OrderBy(w => WorkerOrder((w.Agent ?? string.Empty).Trim().ToLowerInvariant()))
            .ToList();
        foreach (var w in workers)
        {
            ct.ThrowIfCancellationRequested();
            var agent = (w.Agent ?? string.Empty).Trim().ToLowerInvariant();
            if (agent.Length == 0) continue;

            if (outputs.ContainsKey(agent))
                continue;

            switch (agent)
            {
                case "planner":
                {
                    var provider = await EnsureProviderRunnableOrPauseAsync(
                        ctx.Session,
                        ctx.RunId,
                        agent: "planner",
                        stepName: "vibe.planner",
                        resolveProvider: () => resolveProvider("planner"),
                        emitAssistantDelta: ctx.EmitAssistantDelta,
                        ct: ct);
                    var (plannerOutput, plannerPrompt) = await RunPlannerAsync(
                        ctx,
                        getDagSnapshot(),
                        provider,
                        ct);
                    outputs[agent] = plannerOutput;
                    if (plannerPrompt != null) promptRecords[agent] = plannerPrompt;
                    break;
                }
                case "reasoner":
                {
                    var provider = await EnsureProviderRunnableOrPauseAsync(
                        ctx.Session,
                        ctx.RunId,
                        agent: "reasoner",
                        stepName: "vibe.reasoner",
                        resolveProvider: () => resolveProvider("reasoner"),
                        emitAssistantDelta: ctx.EmitAssistantDelta,
                        ct: ct);
                    var (reasonerOutput, reasonerPrompt) = await RunReasonerAsync(
                        ctx,
                        getDagSnapshot(),
                        outputs.TryGetValue("planner", out var p) ? p : null,
                        provider,
                        ct);
                    outputs[agent] = reasonerOutput;
                    if (reasonerPrompt != null) promptRecords[agent] = reasonerPrompt;
                    break;
                }
                case "librarian":
                {
                    var provider = await EnsureProviderRunnableOrPauseAsync(
                        ctx.Session,
                        ctx.RunId,
                        agent: "librarian",
                        stepName: "vibe.librarian",
                        resolveProvider: () => resolveProvider("librarian"),
                        emitAssistantDelta: ctx.EmitAssistantDelta,
                        ct: ct);
                    var (librarianOutput, librarianPrompt) = await RunLibrarianAsync(
                        ctx,
                        getDagSnapshot(),
                        provider,
                        ct);
                    outputs[agent] = librarianOutput;
                    if (librarianPrompt != null) promptRecords[agent] = librarianPrompt;

                    await ApplyLibrarianSideEffectsAsync(
                        ctx,
                        outputs,
                        librarianAxioms,
                        factsWritten,
                        ct);
                    break;
                }
                case "verifier":
                {
                    var provider = await EnsureProviderRunnableOrPauseAsync(
                        ctx.Session,
                        ctx.RunId,
                        agent: "verifier",
                        stepName: "vibe.verifier",
                        resolveProvider: () => resolveProvider("verifier"),
                        emitAssistantDelta: ctx.EmitAssistantDelta,
                        ct: ct);

                    // Check if multi-stage verification is enabled (default: true)
                    var useMultiStage = _core.Configuration?.GetValue<bool?>("Vibe:MultiStageVerification:Enabled") ?? true;

                    if (useMultiStage)
                    {
                        // Multi-stage verification: Scout (2 workers) + Prover (5 workers)
                        var multiStageResult = await RunMultiStageVerifierAsync(
                            ctx,
                            getDagSnapshot(),
                            outputs.TryGetValue("reasoner", out var reasonerOut) ? reasonerOut : null,
                            provider,
                            ct);
                        outputs[agent] = multiStageResult.Summary;

                        // Store verification pass/fail status for downstream use
                        outputs["verifier_passed"] = multiStageResult.OverallPass.ToString();
                        
                        // Save verifier prompt record
                        if (multiStageResult.PromptRecord != null)
                            promptRecords[agent] = multiStageResult.PromptRecord;
                    }
                    else
                    {
                        // Legacy single-pass verification
                        var (verifierOutput, verifierPrompt) = await RunVerifierAsync(
                            ctx,
                            getDagSnapshot(),
                            outputs.TryGetValue("reasoner", out var r) ? r : null,
                            provider,
                            ct);
                        outputs[agent] = verifierOutput;
                        if (verifierPrompt != null) promptRecords[agent] = verifierPrompt;
                    }
                    break;
                }
                case "dag_builder":
                {
                    // Refresh DAG snapshot right before builder (other sessions may have mutated the shared DAG).
                    var fresh = await _core.Dag.LoadSnapshotAsync(dagId, ct);
                    onDagRefreshed(fresh);

                    var provider = await EnsureProviderRunnableOrPauseAsync(
                        ctx.Session,
                        ctx.RunId,
                        agent: "dag_builder",
                        stepName: "vibe.dag_builder",
                        resolveProvider: () => resolveProvider("dag_builder"),
                        emitAssistantDelta: ctx.EmitAssistantDelta,
                        ct: ct);
                    outputs[agent] = await RunDagBuilderAsync(
                        ctx,
                        fresh,
                        outputs,
                        librarianAxioms,
                        provider,
                        ct);
                    break;
                }
                default:
                    // Unknown agent name in plan: ignore (MVP).
                    break;
            }
        }

        // Note: promptRecords are collected and will be saved by the caller (ExecuteOneRoundAsync)
    }

    private async Task ApplyLibrarianSideEffectsAsync(
        VibeRoundContext ctx,
        IReadOnlyDictionary<string, string> outputs,
        List<LibrarianAxiomCandidate> librarianAxioms,
        List<string> factsWritten,
        CancellationToken ct)
    {
        if (!outputs.TryGetValue("librarian", out var libOut))
            return;

        try
        {
            var actions = TryParseLibrarianActions(libOut);
            if (actions == null)
                return;

            if (actions.AxiomsForDag is { Count: > 0 })
            {
                librarianAxioms.Clear();
                librarianAxioms.AddRange(actions.AxiomsForDag);
            }

            if (actions.FactsWrite is { Count: > 0 })
            {
                var written = await TryWriteFactsAsync(ctx.Session, actions.FactsWrite, ct);
                if (written.Count > 0)
                {
                    factsWritten.AddRange(written);

                    // Surface the write as an explicit system-style delta in the main assistant stream.
                    EmitSection(ctx.EmitAssistantDelta, "### Librarian wrote DAG facts\n");
                    foreach (var p in written)
                        ctx.EmitAssistantDelta($"- {p}\n");
                    ctx.EmitAssistantDelta("\n");
                }
            }
        }
        catch
        {
            // best-effort only
        }
    }

    // ============================================================
    //  Agent Prompt Logger (per-session file)
    // ============================================================

    /// <summary>
    /// Saves agent prompts to a Markdown file in /Users/chronoai/.aevatar/prompt_logs/.
    /// Each run appends a new section with all agent prompts (system prompt, user prompt, materials context, and output).
    /// </summary>
    internal async Task SaveAgentPromptsToFileAsync(
        string sessionId,
        string runId,
        string question,
        Dictionary<string, AgentPromptRecord> promptRecords,
        CancellationToken ct)
    {
        if (promptRecords == null || promptRecords.Count == 0)
            return;

        try
        {
            // Save to fixed directory: /Users/chronoai/.aevatar/prompt_logs/
            var promptLogsDir = "/Users/chronoai/.aevatar/prompt_logs";
            Directory.CreateDirectory(promptLogsDir);

            var filename = $"prompts_{sessionId}.md";
            var filepath = Path.Combine(promptLogsDir, filename);

            // Build Markdown content for this run
            var sb = new StringBuilder(4096);
            sb.AppendLine($"# Run: {runId}");
            sb.AppendLine();
            sb.AppendLine($"**Timestamp**: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"**Question**: {question}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // Order agents: research_assistant (if any) -> planner -> reasoner -> verifier -> others
            var agentOrder = new[] { "research_assistant", "planner", "reasoner", "verifier", "librarian", "dag_builder" };
            var orderedAgents = promptRecords.Keys
                .OrderBy(a => Array.IndexOf(agentOrder, a.ToLowerInvariant()) >= 0 
                    ? Array.IndexOf(agentOrder, a.ToLowerInvariant()) 
                    : int.MaxValue)
                .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var agentName in orderedAgents)
            {
                if (!promptRecords.TryGetValue(agentName, out var record))
                    continue;

                sb.AppendLine($"## {agentName}");
                sb.AppendLine();
                sb.AppendLine($"**Timestamp**: {record.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine();

                // System Prompt
                sb.AppendLine("<details>");
                sb.AppendLine("<summary><strong>System Prompt</strong></summary>");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(EscapeMarkdown(record.SystemPrompt));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();

                // Materials Context (if present)
                if (!string.IsNullOrWhiteSpace(record.MaterialsContext))
                {
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary><strong>Materials Context</strong></summary>");
                    sb.AppendLine();
                    sb.AppendLine("```text");
                    sb.AppendLine(EscapeMarkdown(record.MaterialsContext));
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                // User Prompt
                sb.AppendLine("<details>");
                sb.AppendLine("<summary><strong>User Prompt</strong></summary>");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(EscapeMarkdown(record.UserPrompt));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();

                // Output
                sb.AppendLine("<details>");
                sb.AppendLine("<summary><strong>Output</strong></summary>");
                sb.AppendLine();
                
                // Determine if output is JSON or Markdown
                var output = record.RawOutput ?? string.Empty;
                var isJson = output.TrimStart().StartsWith("{") || output.TrimStart().StartsWith("[");
                var lang = isJson ? "json" : "markdown";
                
                sb.AppendLine($"```{lang}");
                sb.AppendLine(EscapeMarkdown(output));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            // Append to file (create if not exists)
            var content = sb.ToString();
            await File.AppendAllTextAsync(filepath, content, Encoding.UTF8, ct);
        }
        catch (Exception ex)
        {
            _host.Logger.LogWarning(ex, "[VibeOrchestrator] Failed to save agent prompts to file (best-effort).");
        }
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        
        // Basic markdown escaping for code blocks
        return text.Replace("```", "\\`\\`\\`");
    }
}


