using Aevatar.Agents.AGUI;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Sessions;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
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

            // Execute pivot if high-confidence direction change detected
            if (pivotIntent is { IsDirectionChange: true } &&
                pivotIntent.Confidence >= _pivot.Options.ConfidenceThreshold)
            {
                try
                {
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
                        ctx.EmitAssistantDelta("\n⏳ 研究方向更新队列已满，请稍后重试...\n\n");
                    }
                    else if (queueResult.ErrorMessage != null)
                    {
                        _host.Logger.LogError(
                            "Pivot failed for session {SessionId}: {Error}",
                            session.Id, queueResult.ErrorMessage);
                        ctx.EmitAssistantDelta("\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                    }
                    else if (queueResult.Operation != null)
                    {
                        var pivotOp = queueResult.Operation;

                        _host.Logger.LogInformation(
                            "Pivot completed for session {SessionId}: cancelled={Cancelled}, preserved={Preserved}, queued={Queued}",
                            session.Id, pivotOp.CancelledNodeIds.Count, pivotOp.PreservedNodeIds.Count, queueResult.Queued);

                        // Emit completion event
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
                    ctx.EmitAssistantDelta("\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                    // Don't block the round - pivot failure is non-fatal
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

    private async Task<(PlanResult Plan, SraDagSnapshot DagSnapshot)> RunPlanPhaseAsync(
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

        var plan = await TryGetPlanAsync(
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

        return (plan, dagSnap);
    }

    private async Task<(Dictionary<string, string> Outputs, bool MeshUsed)> RunWorkerPhaseAsync(
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
                ct,
                onDagRefreshed: s => localDagSnap = s,
                getDagSnapshot: () => localDagSnap);
        }

        return (outputs, meshUsed);
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
                          new() { Agent = "librarian", Task = "List key evidence and missing gaps" },
                          new() { Agent = "verifier", Task = "Verify reasoning correctness and identify gaps" },
                          new() { Agent = "dag_builder", Task = "Propose a DAG mutation candidate in strict JSON" }
                      };

        // Deterministic ordering (helps librarian->dag_builder handoff).
        workers = workers
            .OrderBy(w => WorkerOrder((w.Agent ?? string.Empty).Trim().ToLowerInvariant()))
            .ToList();

        // Run workers (best-effort; keep outputs bounded).
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
                    outputs[agent] = await RunPlannerAsync(
                        ctx,
                        getDagSnapshot(),
                        provider,
                        ct);
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
                    outputs[agent] = await RunReasonerAsync(
                        ctx,
                        getDagSnapshot(),
                        outputs.TryGetValue("planner", out var p) ? p : null,
                        provider,
                        ct);
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
                    outputs[agent] = await RunLibrarianAsync(
                        ctx,
                        getDagSnapshot(),
                        provider,
                        ct);

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
                    }
                    else
                    {
                        // Legacy single-pass verification
                        outputs[agent] = await RunVerifierAsync(
                            ctx,
                            getDagSnapshot(),
                            outputs.TryGetValue("reasoner", out var r) ? r : null,
                            provider,
                            ct);
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
}


