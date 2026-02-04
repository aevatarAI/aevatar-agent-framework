using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe.Pivot;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingRoundServices
{
    // ============================================================
    //  ExecuteOneRoundAsync helpers (split to keep main file small)
    // ============================================================

    public sealed class ResearchingRoundContext
    {
        public ResearchingRoundContext(
            ResearchSession session,
            string runId,
            ResearchingInput input,
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
        public ResearchingInput Input { get; }
        public string Question { get; }
        public MaterialsSnapshot Materials { get; }
        public Action<string> EmitAssistantDelta { get; }
    }

    private async Task RunPivotDetectionAsync(ResearchingRoundContext ctx, CancellationToken ct)
    {
        var session = ctx.Session;
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

            if (pivotIntent == null)
            {
                _host.Logger.LogDebug(
                    "Pivot detection returned null for session {SessionId}",
                    session.Id);
                return;
            }

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
        finally { }
    }

    private async Task<(PlanResult Plan, SraDagSnapshot DagSnapshot)> RunPlanPhaseAsync(
        ResearchingRoundContext ctx,
        string dagId,
        SraDagSnapshot dagSnap,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? raProvider,
        CancellationToken ct)
    {
        var session = ctx.Session;
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

        return (plan, dagSnap);
    }

    internal async Task<Dictionary<string, string>> RunWorkerPhaseAsync(
        WorkflowRunContext runContext,
        ResearchingRoundContext ctx,
        string dagId,
        Func<string, string?> resolveProvider,
        CancellationToken ct)
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await RunMeshWorkerPhaseAsync(
            runContext,
            ctx,
            dagId,
            resolveProvider,
            outputs,
            ct);

        return outputs;
    }

    private async Task RunMeshWorkerPhaseAsync(
        WorkflowRunContext runContext,
        ResearchingRoundContext ctx,
        string dagId,
        Func<string, string?> resolveProvider,
        Dictionary<string, string> outputs,
        CancellationToken ct)
    {
        var meshOpt = _mesh.Options.Value;
        if (!meshOpt.Enabled)
        {
            outputs["mesh_error_kind"] = "mesh.disabled";
            outputs["mesh_error"] = "mesh orchestration disabled";

            ctx.Session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.mesh_disabled",
                Value = new { sessionId = ctx.Session.Id, runId = ctx.RunId }
            });

            EmitSection(ctx.EmitAssistantDelta, "### Mesh orchestration disabled; worker phase skipped\n");
            ctx.EmitAssistantDelta("_(mesh disabled)_\n\n");
            return;
        }
        try
        {
            var raw = await TryLoadOrSeedMeshAsync(ctx.Session, ctx.RunId, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                outputs["mesh_error_kind"] = "mesh.missing";
                outputs["mesh_error"] = "mesh.yaml/json missing and auto-seed failed";

                ctx.Session.Events.Publish(new CustomEvent
                {
                    Timestamp = NowMs(),
                    Name = "aevatar.vibe.mesh_missing",
                    Value = new { sessionId = ctx.Session.Id, runId = ctx.RunId }
                });

                EmitSection(ctx.EmitAssistantDelta, "### Mesh orchestration missing; worker phase skipped\n");
                ctx.EmitAssistantDelta("mesh.yaml/json missing and auto-seed failed.\n\n");
                return;
            }

            var compile = _mesh.Compiler.Compile(raw!);
            if (!compile.Ok || compile.Definition == null)
            {
                HandleMeshErrors(
                    ctx.Session,
                    ctx.EmitAssistantDelta,
                    kind: "mesh.compile_failed",
                    errors: compile.Errors,
                    outputs: outputs);
                return;
            }

            var planRes = _mesh.Planner.Plan(ctx.Session.Id, ctx.RunId, compile.Definition);
            if (!planRes.Ok || planRes.Plan == null)
            {
                HandleMeshErrors(
                    ctx.Session,
                    ctx.EmitAssistantDelta,
                    kind: "mesh.plan_failed",
                    errors: planRes.Errors,
                    outputs: outputs);
                return;
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
                var p = await _llmGate.EnsureProviderRunnableOrPauseAsync(
                    runContext,
                    agent: role,
                    stepName: $"vibe.mesh.{role}",
                    resolveProvider: () => resolveProvider(role),
                    ct: ct);

                if (!string.IsNullOrWhiteSpace(p))
                    meshProviderMap[role] = p!;
            }

            string? ResolveProviderForMesh(string role) =>
                meshProviderMap.TryGetValue(role, out var p) ? p : resolveProvider(role);

            // Keep DAG snapshot fresh at the beginning of mesh execution.
            var freshDag = await _core.Dag.LoadSnapshotAsync(dagId, ct);

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

            // Best-effort artifacts for debugging/replay.
            TryWriteMeshRunArtifacts(ctx.Session.Id, ctx.RunId, raw!, planRes.Plan, run);

            return;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            HandleMeshErrors(
                ctx.Session,
                ctx.EmitAssistantDelta,
                kind: "mesh.exception",
                errors: [new Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError("mesh.exception", ex.Message, null)],
                outputs: outputs);
            return;
        }
    }

}
