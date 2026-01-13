using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.Core.Secrets;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Mesh;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

// ============================================================
//  VibeOrchestrator (MVP)
//
//  Goal:
//  - Run one vibe round end-to-end:
//      research_assistant plan → workers → maker-v2 consensus → DAG + Trace
//
//  Notes:
//  - Best-effort: never crash server due to orchestration/projection.
//  - Streaming: caller provides emitAssistantDelta() that projects to AG-UI.
// ============================================================

internal sealed partial class VibeOrchestrator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ResearchRuntime _runtime;
    private readonly MaterialsService _materials;
    private readonly WorkspaceService _workspace;
    private readonly BriefStore _brief;
    private readonly DeliveryCenterStore _delivery;
    private readonly DagStore _dag;
    private readonly IDagGroundingPolicy _dagGrounding;
    private readonly TraceStore _trace;
    private readonly FileMailboxService _mailbox;
    private readonly PaperService _paper;
    private readonly AgentProvidersStore _agentProviders;
    private readonly IAevatarUserSecretsStore _secrets;
    private readonly IOptions<MeshOrchestrationOptions> _meshOptions;
    private readonly MeshDefinitionStore _meshStore;
    private readonly MeshCompilerService _meshCompiler;
    private readonly MeshExecutionPlanner _meshPlanner;
    private readonly MeshExecutionRunner _meshRunner;
    private readonly IHostEnvironment _env;
    private readonly ILogger<VibeOrchestrator> _logger;

    public VibeOrchestrator(
        ResearchRuntime runtime,
        MaterialsService materials,
        WorkspaceService workspace,
        BriefStore brief,
        DeliveryCenterStore delivery,
        DagStore dag,
        IDagGroundingPolicy dagGrounding,
        TraceStore trace,
        FileMailboxService mailbox,
        PaperService paper,
        AgentProvidersStore agentProviders,
        IAevatarUserSecretsStore secrets,
        IOptions<MeshOrchestrationOptions> meshOptions,
        MeshDefinitionStore meshStore,
        MeshCompilerService meshCompiler,
        MeshExecutionPlanner meshPlanner,
        MeshExecutionRunner meshRunner,
        IHostEnvironment env,
        ILogger<VibeOrchestrator> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _dagGrounding = dagGrounding ?? throw new ArgumentNullException(nameof(dagGrounding));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _agentProviders = agentProviders ?? throw new ArgumentNullException(nameof(agentProviders));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _meshOptions = meshOptions ?? throw new ArgumentNullException(nameof(meshOptions));
        _meshStore = meshStore ?? throw new ArgumentNullException(nameof(meshStore));
        _meshCompiler = meshCompiler ?? throw new ArgumentNullException(nameof(meshCompiler));
        _meshPlanner = meshPlanner ?? throw new ArgumentNullException(nameof(meshPlanner));
        _meshRunner = meshRunner ?? throw new ArgumentNullException(nameof(meshRunner));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteOneRoundAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(materials);
        emitAssistantDelta ??= _ => { };

        // ------------------------------------------------------------
        // Shared DAG binding (cross-session)
        // ------------------------------------------------------------
        var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();

        // Load File-SSoT context (best-effort).
        var dagSnap = await _dag.LoadSnapshotAsync(dagId, ct);
        var recentTrace = await _trace.LoadLatestAsync(session.Id, max: 5, ct);

        // ------------------------------------------------------------
        // Per-agent LLM provider mapping (File-SSoT)
        //
        // Rules:
        // - providerOverride (request-level) wins for all agents
        // - else use agent_providers.json mapping
        // - else fallback to session.ProviderName
        // - runtime resolves a final default if still empty/invalid
        // ------------------------------------------------------------
        AgentProvidersStore.AgentProvidersSnapshot? agentProvidersSnap = null;
        try { agentProvidersSnap = await _agentProviders.LoadAsync(session.Id, ct); } catch { /* best-effort */ }
        var agentProviderMap = agentProvidersSnap?.Map ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? ResolveProvider(string agent)
        {
            if (!string.IsNullOrWhiteSpace(providerOverride))
                return providerOverride;
            if (agentProviderMap.TryGetValue(agent, out var p) && !string.IsNullOrWhiteSpace(p))
                return p;
            return session.ProviderName;
        }

        var raProvider = ResolveProvider("research_assistant");
        var plannerProvider = ResolveProvider("planner");
        var reasonerProvider = ResolveProvider("reasoner");
        var librarianProvider = ResolveProvider("librarian");
        var verifierProvider = ResolveProvider("verifier");
        var dagBuilderProvider = ResolveProvider("dag_builder");
        var paperEditorProvider = ResolveProvider("paper_editor");

        // ------------------------------------------------------------
        // Pause gate: if provider is missing apiKey, wait for user to configure then continue.
        //
        // 中文说明：
        // - 只在真正要调用 LLM 之前做 gate（materials/dag/trace 都不需要 key）。
        // - 这里先 gate research_assistant（BRIEF/PLAN/SUMMARY 都依赖它）。
        // ------------------------------------------------------------
        raProvider = await EnsureProviderRunnableOrPauseAsync(
            session,
            runId,
            agent: "research_assistant",
            stepName: "vibe",
            resolveProvider: () => ResolveProvider("research_assistant"),
            emitAssistantDelta: emitAssistantDelta,
            ct: ct);

        // Attach metadata to the main assistant message so UI can show provider per agent card.
        // (Message id scheme matches ResearchRunExecutor: msg:{sessionId}:assistant:{runId})
        try
        {
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.message_meta",
                Value = new
                {
                    messageId = $"msg:{session.Id}:assistant:{runId}",
                    agent = "research_assistant",
                    stepName = "vibe",
                    providerName = (raProvider ?? string.Empty).Trim()
                }
            });
        }
        catch
        {
            // best-effort only
        }

        // Librarian side-effects (facts/axioms) collected during this round.
        var librarianAxioms = new List<LibrarianAxiomCandidate>();
        var factsWritten = new List<string>();

        // ------------------------------------------------------------
        // Step: Research Brief (1 page) - generate once if missing
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.brief" });
        try
        {
            var existing = await _brief.LoadAsync(session.Id, ct);
            if (existing.Version <= 0)
            {
                var brief = await TryGetBriefAsync(session.Id, input, question, materials, dagSnap, recentTrace, raProvider, ct);
                if (brief != null)
                {
                    // First brief for a session: start at version=1.
                    brief.Version = 1;
                    var saved = await _brief.SaveAsync(session.Id, brief, ct);

                    // UI: notify brief updated (best-effort)
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = NowMs(),
                        Name = "aevatar.vibe.brief_updated",
                        Value = new { sessionId = session.Id, version = saved.Version }
                    });

                    // Also surface a human-friendly excerpt into the chat stream.
                    EmitSection(emitAssistantDelta, "### Research Brief (1 page)\n");
                    emitAssistantDelta(RenderBriefMarkdown(saved));
                    emitAssistantDelta("\n\n");

                    // ------------------------------------------------------------
                    // Persist brief milestones into DAG (as multiple "plan" nodes)
                    //
                    // 中文说明：
                    // - 以前 goals 是一次产出多个条目；现在用 brief.milestones 作为“多条 plan”
                    // - 这些 plan 节点用于全局 roadmap（并不会污染 knowledge grounding）
                    // ------------------------------------------------------------
                    try
                    {
                        var mm = BuildMilestonesPlanDagMutation(session.Id, runId, question, saved);
                        if (mm != null)
                        {
                            dagSnap = await _dag.ApplyMutationAsync(dagId, mm, ct);
                            session.Events.Publish(new CustomEvent
                            {
                                Timestamp = NowMs(),
                                Name = "aevatar.vibe.milestones_plan_dag_written",
                                Value = new { sessionId = session.Id, dagId, runId, mutationId = mm.MutationId, milestones = saved.Milestones.Count }
                            });
                        }
                    }
                    catch
                    {
                        // best-effort only
                    }
                }
            }
        }
        catch
        {
            // best-effort only
        }
        finally
        {
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.brief" });
        }

        // ------------------------------------------------------------
        // Step: research_assistant plan (JSON)
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.ra_plan"
        });

        var plan = await TryGetPlanAsync(session.Id, input, question, materials, dagSnap, recentTrace, raProvider, ct);
        if (!string.IsNullOrWhiteSpace(plan.RawJson))
        {
            EmitSection(emitAssistantDelta, "### Plan (research_assistant)\n");
            emitAssistantDelta("```json\n");
            emitAssistantDelta(plan.RawJson!.Trim() + "\n");
            emitAssistantDelta("```\n\n");
        }

        // ------------------------------------------------------------
        // Step: Persist plan into DAG (as "plan" nodes)
        //
        // 中文说明：
        // - vibe researching 的目标是“往 DAG 上增量写知识”
        // - 但在开始研究前，我们先把本轮 plan 落到 DAG（便于审阅/回放/可视化）
        // - plan 节点不走 verifier-quorum 共识（MVP）；仍保持知识写入走共识门控
        // ------------------------------------------------------------
        try
        {
            var m = BuildPlanDagMutation(session.Id, runId, question, plan);
            if (m != null)
            {
                dagSnap = await _dag.ApplyMutationAsync(dagId, m, ct);
                session.Events.Publish(new CustomEvent
                {
                    Timestamp = NowMs(),
                    Name = "aevatar.vibe.plan_dag_written",
                    Value = new { sessionId = session.Id, dagId, runId, mutationId = m.MutationId }
                });
            }
        }
        catch
        {
            // best-effort only
        }

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.ra_plan"
        });

        // ------------------------------------------------------------
        // Worker phase: mesh-driven (Option B) OR fallback (current hardcoded/plan workers)
        // ------------------------------------------------------------
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var meshUsed = false;
        var meshOpt = _meshOptions.Value;
        if (meshOpt.Enabled)
        {
            try
            {
                var raw = await TryLoadOrSeedMeshAsync(session, runId, ct);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var compile = _meshCompiler.Compile(raw!);
                    if (compile.Ok && compile.Definition != null)
                    {
                        var planRes = _meshPlanner.Plan(session.Id, runId, compile.Definition);
                        if (planRes.Ok && planRes.Plan != null)
                        {
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
                                    session,
                                    runId,
                                    agent: role,
                                    stepName: $"vibe.mesh.{role}",
                                    resolveProvider: () => ResolveProvider(role),
                                    emitAssistantDelta: emitAssistantDelta,
                                    ct: ct);

                                if (!string.IsNullOrWhiteSpace(p))
                                    meshProviderMap[role] = p!;
                            }

                            string? ResolveProviderForMesh(string role) =>
                                meshProviderMap.TryGetValue(role, out var p) ? p : ResolveProvider(role);

                            // Keep DAG snapshot fresh at the beginning of mesh execution.
                            dagSnap = await _dag.LoadSnapshotAsync(dagId, ct);

                            var run = await _meshRunner.ExecuteAsync(
                                session,
                                input,
                                materials,
                                dagSnap,
                                planRes.Plan,
                                question,
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
                            TryWriteMeshRunArtifacts(session.Id, runId, raw!, planRes.Plan, run);
                        }
                        else
                        {
                            HandleMeshErrorsOrFallback(
                                session,
                                emitAssistantDelta,
                                kind: "mesh.plan_failed",
                                errors: planRes.Errors,
                                options: meshOpt,
                                outputs: outputs,
                                out meshUsed);
                        }
                    }
                    else
                    {
                        HandleMeshErrorsOrFallback(
                            session,
                            emitAssistantDelta,
                            kind: "mesh.compile_failed",
                            errors: compile.Errors,
                            options: meshOpt,
                            outputs: outputs,
                            out meshUsed);
                    }
                }
                else
                {
                    // Mesh enabled but mesh.{yaml|json} absent AND cannot seed → fall back silently (best-effort event only).
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = NowMs(),
                        Name = "aevatar.vibe.mesh_missing",
                        Value = new { sessionId = session.Id, runId }
                    });
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && ct.IsCancellationRequested)
                    throw;
                HandleMeshErrorsOrFallback(
                    session,
                    emitAssistantDelta,
                    kind: "mesh.exception",
                    errors: [new Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError("mesh.exception", ex.Message, null)],
                    options: meshOpt,
                    outputs: outputs,
                    out meshUsed);
            }
        }

        if (!meshUsed)
        {
            // Worker roster (fallback-first).
            var workers = plan.Workers?.Where(w => !string.IsNullOrWhiteSpace(w.Agent)).ToList()
                          ?? new List<PlanWorker>
                          {
                              new() { Agent = "planner", Task = "Produce an executable plan and unknowns" },
                              new() { Agent = "reasoner", Task = "Provide grounded reasoning with explicit hypotheses" },
                              new() { Agent = "librarian", Task = "List key evidence and missing sources" },
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
                        plannerProvider = await EnsureProviderRunnableOrPauseAsync(
                            session,
                            runId,
                            agent: "planner",
                            stepName: "vibe.planner",
                            resolveProvider: () => ResolveProvider("planner"),
                            emitAssistantDelta: emitAssistantDelta,
                            ct: ct);
                        outputs[agent] = await RunPlannerAsync(session, runId, input, question, materials, dagSnap, plannerProvider, ct);
                        break;
                    case "reasoner":
                        reasonerProvider = await EnsureProviderRunnableOrPauseAsync(
                            session,
                            runId,
                            agent: "reasoner",
                            stepName: "vibe.reasoner",
                            resolveProvider: () => ResolveProvider("reasoner"),
                            emitAssistantDelta: emitAssistantDelta,
                            ct: ct);
                        outputs[agent] = await RunReasonerAsync(session, runId, input, question, materials, dagSnap, outputs.TryGetValue("planner", out var p) ? p : null, reasonerProvider, ct);
                        break;
                    case "librarian":
                        librarianProvider = await EnsureProviderRunnableOrPauseAsync(
                            session,
                            runId,
                            agent: "librarian",
                            stepName: "vibe.librarian",
                            resolveProvider: () => ResolveProvider("librarian"),
                            emitAssistantDelta: emitAssistantDelta,
                            ct: ct);
                        outputs[agent] = await RunLibrarianAsync(session, runId, input, question, materials, dagSnap, librarianProvider, ct);
                        break;
                    case "verifier":
                        verifierProvider = await EnsureProviderRunnableOrPauseAsync(
                            session,
                            runId,
                            agent: "verifier",
                            stepName: "vibe.verifier",
                            resolveProvider: () => ResolveProvider("verifier"),
                            emitAssistantDelta: emitAssistantDelta,
                            ct: ct);
                        outputs[agent] = await RunVerifierAsync(session, runId, input, question, materials, dagSnap, outputs.TryGetValue("reasoner", out var r) ? r : null, verifierProvider, ct);
                        break;
                    case "dag_builder":
                        // Refresh DAG snapshot right before builder (other sessions may have mutated the shared DAG).
                        dagSnap = await _dag.LoadSnapshotAsync(dagId, ct);
                        dagBuilderProvider = await EnsureProviderRunnableOrPauseAsync(
                            session,
                            runId,
                            agent: "dag_builder",
                            stepName: "vibe.dag_builder",
                            resolveProvider: () => ResolveProvider("dag_builder"),
                            emitAssistantDelta: emitAssistantDelta,
                            ct: ct);
                        outputs[agent] = await RunDagBuilderAsync(session, runId, input, question, materials, dagSnap, outputs, librarianAxioms, dagBuilderProvider, ct);
                        break;
                    default:
                        // Unknown agent name in plan: ignore (MVP).
                        break;
                }

                // Post-hook: librarian may propose side effects (facts/goals/axioms).
                if (string.Equals(agent, "librarian", StringComparison.OrdinalIgnoreCase) &&
                    outputs.TryGetValue("librarian", out var libOut))
                {
                    try
                    {
                        var actions = TryParseLibrarianActions(libOut);
                        if (actions != null)
                        {
                            if (actions.AxiomsForDag is { Count: > 0 })
                            {
                                librarianAxioms.Clear();
                                librarianAxioms.AddRange(actions.AxiomsForDag);
                            }

                            if (actions.FactsWrite is { Count: > 0 })
                            {
                                var written = await TryWriteFactsAsync(session.Id, actions.FactsWrite, ct);
                                if (written.Count > 0)
                                {
                                    factsWritten.AddRange(written);

                                    // Surface the write as an explicit system-style delta in the main assistant stream.
                                    EmitSection(emitAssistantDelta, "### Librarian wrote facts\n");
                                    foreach (var p in written)
                                        emitAssistantDelta($"- {p}\n");
                                    emitAssistantDelta("\n");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // best-effort only
                    }
                }
            }
        }

        // ------------------------------------------------------------
        // Step: DAG apply (no consensus)
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.dag_consensus"
        });

        // Refresh again before apply (shared DAG).
        dagSnap = await _dag.LoadSnapshotAsync(dagId, ct);
        var dagResult = await RunDagApplyAsync(session, runId, question, materials, dagSnap, outputs, emitAssistantDelta, ct);

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.dag_consensus"
        });

        // ------------------------------------------------------------
        // Step: Delivery center update (paper + lists) via paper_editor
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.delivery" });

        DeliveryUpdateResult? deliveryUpdate = null;
        try
        {
            // Only run paper_editor when DAG consensus accepted something (MVP).
            // Future: also update lists even when no DAG change occurred.
            if (dagResult.Accepted && dagResult.AcceptedMutation != null)
            {
                paperEditorProvider = await EnsureProviderRunnableOrPauseAsync(
                    session,
                    runId,
                    agent: "paper_editor",
                    stepName: "vibe.paper_editor",
                    resolveProvider: () => ResolveProvider("paper_editor"),
                    emitAssistantDelta: emitAssistantDelta,
                    ct: ct);
                var paperEditorOut = await RunPaperEditorAsync(
                    session,
                    runId,
                    input,
                    question,
                    materials,
                    dagResult,
                    outputs,
                    paperEditorProvider,
                    ct);

                outputs["paper_editor"] = paperEditorOut;

                deliveryUpdate = await TryApplyPaperEditorOutputAsync(session, runId, paperEditorOut, ct);

                if (deliveryUpdate != null)
                {
                    EmitSection(emitAssistantDelta, "### Delivery Center Updated\n");
                    emitAssistantDelta($"- paperPatchesApplied={deliveryUpdate.PatchesApplied}, listsWritten={deliveryUpdate.ListsWritten}\n");
                    if (!string.IsNullOrWhiteSpace(deliveryUpdate.ChangedSummary))
                        emitAssistantDelta($"- summary: {Bound(deliveryUpdate.ChangedSummary!, 500)}\n");
                    emitAssistantDelta("\n");
                }
            }
        }
        catch
        {
            // best-effort only
        }
        finally
        {
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.delivery" });
        }

        // ------------------------------------------------------------
        // Step: research_assistant summary → TraceStore
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.summary"
        });

        var summaryMd = await TryGetSummaryAsync(session.Id, input, question, dagResult, outputs, factsWritten, raProvider, ct);
        if (!string.IsNullOrWhiteSpace(summaryMd))
        {
            EmitSection(emitAssistantDelta, "### Round Summary\n");
            emitAssistantDelta(summaryMd!.Trim() + "\n\n");
        }

        await PersistTraceAsync(session, runId, input, question, outputs, dagResult, summaryMd, ct);

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.summary"
        });
    }

    // ============================================================
    //  Mesh integration helpers (Option B)
    // ============================================================

    private void HandleMeshErrorsOrFallback(
        ResearchSession session,
        Action<string> emit,
        string kind,
        IReadOnlyList<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError> errors,
        MeshOrchestrationOptions options,
        IDictionary<string, string> outputs,
        out bool meshUsed)
    {
        meshUsed = false;
        errors ??= Array.Empty<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError>();

        var onErr = (options.OnCompileError ?? string.Empty).Trim().ToLowerInvariant();
        var failFast = onErr is "fail" or "failfast";

        // Publish a best-effort event for observability.
        try
        {
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.mesh_error",
                Value = new
                {
                    sessionId = session.Id,
                    kind,
                    count = errors.Count,
                    onCompileError = onErr
                }
            });
        }
        catch
        {
            // best-effort only
        }

        var rendered = RenderMeshErrors(errors);
        outputs["mesh_error_kind"] = kind;
        outputs["mesh_error"] = rendered;

        if (failFast)
        {
            // Fail-fast means: do NOT run fallback workers; continue the round safely with empty outputs.
            meshUsed = true;
            EmitSection(emit, "### Mesh orchestration failed (fail-fast)\n");
            emit(rendered + "\n\n");
        }
        else
        {
            // Fallback mode: announce once (human-friendly), then proceed with existing worker pipeline.
            EmitSection(emit, "### Mesh orchestration invalid → fallback to default pipeline\n");
            emit(Bound(rendered, 2500) + "\n\n");
        }
    }

    private static string RenderMeshErrors(IReadOnlyList<Aevatar.CognitiveMesh.Dsl.Validation.DslValidationError> errors)
    {
        if (errors is not { Count: > 0 })
            return "_(no errors)_";

        var sb = new StringBuilder(1024);
        sb.AppendLine("Errors:");
        foreach (var e in errors.Take(20))
        {
            var code = (e.Code ?? string.Empty).Trim();
            var path = (e.Path ?? string.Empty).Trim();
            var msg = (e.Message ?? string.Empty).Trim();
            if (code.Length == 0) code = "error";
            if (path.Length > 0)
                sb.Append("- ").Append(code).Append(" @ ").Append(path).Append(": ").AppendLine(msg);
            else
                sb.Append("- ").Append(code).Append(": ").AppendLine(msg);
        }

        if (errors.Count > 20)
            sb.AppendLine($"... ({errors.Count - 20} more)");

        return Bound(sb.ToString().Trim(), 8000);
    }

    private void TryWriteMeshRunArtifacts(
        string sessionId,
        string runId,
        string rawMeshJson,
        MeshExecutionPlan plan,
        MeshRunResult run)
    {
        try
        {
            var ws = _workspace.EnsureSessionWorkspace(sessionId);
            var dir = Path.Combine(ws.ArtifactsDir, "mesh");
            Directory.CreateDirectory(dir);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");

            var meshPath = Path.Combine(dir, $"{runId}_{stamp}.mesh.json");
            File.WriteAllText(meshPath, rawMeshJson, Encoding.UTF8);

            var planPath = Path.Combine(dir, $"{runId}_{stamp}.plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, Json), Encoding.UTF8);

            var outputsPath = Path.Combine(dir, $"{runId}_{stamp}.outputs.json");
            File.WriteAllText(outputsPath, JsonSerializer.Serialize(run, Json), Encoding.UTF8);
        }
        catch
        {
            // best-effort only
        }
    }

    private static SraDagMutation? BuildPlanDagMutation(
        string sessionId,
        string runId,
        string? question,
        PlanResult plan)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
            return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // Keep plan nodes unique even under shared DAG (multiple sessions writing into one dagId).
        // runId already includes sessionId in "{sessionId}:{seq}" form, so this is collision-resistant.
        var nodeId = SanitizeId($"plan_{runId}");
        if (nodeId.Length == 0)
            nodeId = $"plan_{Guid.NewGuid():N}";

        var label = string.IsNullOrWhiteSpace(plan.RoundTitle)
            ? $"Plan ({Bound(question ?? string.Empty, 120)})"
            : $"Plan: {Bound(plan.RoundTitle!, 180)}";

        var proof = BuildPlanProof(question, plan.Workers);

        var node = new SraDagNode
        {
            Id = nodeId,
            Type = SraDagNodeType.Assumption,
            Label = Bound(label, 200),
            Proof = Bound(proof, 1200),
            UpdatedAt = now,
            Kind = SraDagNodeKind.Plan
        };
        // Tags are optional; graph backend persists Kind separately, but tags are still useful in mutation artifacts.
        node.Tags["runId"] = runId;
        node.Tags["originSessionId"] = sessionId;
        node.Tags["author"] = "research_assistant";
        node.Tags["planKind"] = "round";

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = $"plan_{runId}",
            AuthorAgent = "research_assistant",
            CreatedAt = now
        };
        m.Labels["kind"] = "plan";

        m.UpsertNodes.Add(node);
        // No edges by default: plan nodes are metadata, not derivations.

        return m;
    }

    private static SraDagMutation? BuildMilestonesPlanDagMutation(
        string sessionId,
        string runId,
        string? question,
        SraResearchBriefSnapshot brief)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
            return null;
        if (brief == null || brief.Milestones.Count == 0)
            return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = $"milestones_plan_{runId}",
            AuthorAgent = "research_assistant",
            CreatedAt = now
        };
        m.Labels["kind"] = "plan";
        m.Labels["planKind"] = "milestone";

        // Stable-ish ids: upsert the same milestone nodes across runs for the same session.
        // (If brief is rewritten, these nodes will be updated, not duplicated.)
        var idx = 0;
        foreach (var ms in brief.Milestones.Take(12))
        {
            idx++;
            var roundIndex = ms?.RoundIndex ?? 0;
            var expected = (ms?.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim();
            if (expected.Length == 0) continue;

            var suffix = roundIndex > 0 ? $"r{roundIndex}" : $"i{idx}";
            var nodeId = SanitizeId($"plan_{sessionId}_ms_{suffix}");
            if (nodeId.Length == 0)
                nodeId = $"plan_{Guid.NewGuid():N}";

            var label = roundIndex > 0
                ? $"Milestone (Round {roundIndex}): {Bound(expected, 160)}"
                : $"Milestone: {Bound(expected, 180)}";

            var proofSb = new StringBuilder(256);
            var q = (question ?? string.Empty).Replace("\r", "").Trim();
            if (q.Length > 0) proofSb.AppendLine($"Question: {Bound(q, 600)}");
            if (roundIndex > 0) proofSb.AppendLine($"TargetRound: {roundIndex}");
            proofSb.AppendLine();
            proofSb.AppendLine("ExpectedOutput:");
            proofSb.AppendLine(Bound(expected, 600));

            var node = new SraDagNode
            {
                Id = nodeId,
                Type = SraDagNodeType.Assumption,
                Label = Bound(label, 200),
                Proof = Bound(proofSb.ToString().Trim(), 1200),
                UpdatedAt = now,
                Kind = SraDagNodeKind.Plan
            };

            node.Tags["runId"] = runId;
            node.Tags["originSessionId"] = sessionId;
            node.Tags["author"] = "research_assistant";
            node.Tags["planKind"] = "milestone";
            node.Tags["milestoneRoundIndex"] = roundIndex.ToString();

            m.UpsertNodes.Add(node);
            if (m.UpsertNodes.Count >= 12) break;
        }

        return m.UpsertNodes.Count == 0 ? null : m;
    }

    private static string BuildPlanProof(string? question, List<PlanWorker>? workers)
    {
        var sb = new StringBuilder(512);
        var q = (question ?? string.Empty).Replace("\r", "").Trim();
        if (q.Length > 0)
            sb.AppendLine($"Question: {Bound(q, 600)}");

        if (workers is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Workers:");
            foreach (var w in workers.Take(12))
            {
                if (w == null) continue;
                var agent = (w.Agent ?? string.Empty).Trim();
                var task = (w.Task ?? string.Empty).Replace("\r", "").Trim();
                if (agent.Length == 0 && task.Length == 0) continue;
                sb.Append("- ").Append(agent.Length == 0 ? "worker" : agent);
                if (task.Length > 0) sb.Append(": ").Append(Bound(task, 260));
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        // keep it bounded to avoid huge ids
        var outId = sb.ToString().Trim('_');
        return outId.Length <= 64 ? outId : outId[..64];
    }
}
