using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
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
    private readonly GoalsStore _goals;
    private readonly BriefStore _brief;
    private readonly DeliveryCenterStore _delivery;
    private readonly DagStore _dag;
    private readonly DagConsensusRunner _consensus;
    private readonly TraceStore _trace;
    private readonly FileMailboxService _mailbox;
    private readonly PaperService _paper;
    private readonly ILogger<VibeOrchestrator> _logger;

    public VibeOrchestrator(
        ResearchRuntime runtime,
        MaterialsService materials,
        WorkspaceService workspace,
        GoalsStore goals,
        BriefStore brief,
        DeliveryCenterStore delivery,
        DagStore dag,
        DagConsensusRunner consensus,
        TraceStore trace,
        FileMailboxService mailbox,
        PaperService paper,
        ILogger<VibeOrchestrator> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _consensus = consensus ?? throw new ArgumentNullException(nameof(consensus));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
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

        // Load File-SSoT context (best-effort).
        var goals = await _goals.LoadAsync(session.Id, ct);
        var dagSnap = await _dag.LoadSnapshotAsync(session.Id, ct);
        var recentTrace = await _trace.LoadLatestAsync(session.Id, max: 5, ct);

        // Librarian side-effects (facts/goals/axioms) collected during this round.
        var librarianAxioms = new List<LibrarianAxiomCandidate>();
        var goalSuggestions = new List<GoalCandidate>();
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
                var brief = await TryGetBriefAsync(session.Id, input, question, materials, goals, dagSnap, recentTrace, providerOverride, ct);
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

        var plan = await TryGetPlanAsync(session.Id, input, question, materials, goals, dagSnap, recentTrace, providerOverride, ct);
        if (!string.IsNullOrWhiteSpace(plan.RawJson))
        {
            EmitSection(emitAssistantDelta, "### Plan (research_assistant)\n");
            emitAssistantDelta("```json\n");
            emitAssistantDelta(plan.RawJson!.Trim() + "\n");
            emitAssistantDelta("```\n\n");
        }

        // Auto-init goals if empty (research_assistant may provide goalsInit in plan JSON).
        if (goals.Goals.Count == 0 && plan.GoalsInit is { Count: > 0 })
        {
            var saved = await TrySaveGoalsAsync(
                session,
                existing: goals,
                candidates: plan.GoalsInit,
                updatedBy: "research_assistant",
                reason: "auto_init_empty",
                ct);
            if (saved != null)
            {
                goals = saved;
                EmitSection(emitAssistantDelta, "### Goals initialized (research_assistant)\n");
                foreach (var g in saved.Goals.OrderBy(x => x.Priority).Take(12))
                    emitAssistantDelta($"- ({g.Priority}) {Bound(g.Text ?? string.Empty, 220)}\n");
                emitAssistantDelta("\n");
            }
        }

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.ra_plan"
        });

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
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    outputs[agent] = await RunPlannerAsync(session, runId, input, question, materials, goals, dagSnap, providerOverride, ct);
                    break;
                case "reasoner":
                    outputs[agent] = await RunReasonerAsync(session, runId, input, question, materials, goals, dagSnap, outputs.TryGetValue("planner", out var p) ? p : null, providerOverride, ct);
                    break;
                case "librarian":
                    outputs[agent] = await RunLibrarianAsync(session, runId, input, question, materials, goals, dagSnap, providerOverride, ct);
                    break;
                case "verifier":
                    outputs[agent] = await RunVerifierAsync(session, runId, input, question, materials, goals, dagSnap, outputs.TryGetValue("reasoner", out var r) ? r : null, providerOverride, ct);
                    break;
                case "dag_builder":
                    outputs[agent] = await RunDagBuilderAsync(session, runId, input, question, materials, goals, dagSnap, outputs, librarianAxioms, providerOverride, ct);
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

                        if (actions.GoalSuggestions is { Count: > 0 })
                        {
                            goalSuggestions.AddRange(actions.GoalSuggestions);

                            // If goals are still empty, allow librarian to bootstrap them (best-effort).
                            if (goals.Goals.Count == 0)
                            {
                                var saved = await TrySaveGoalsAsync(
                                    session,
                                    existing: goals,
                                    candidates: actions.GoalSuggestions,
                                    updatedBy: "librarian",
                                    reason: "librarian_suggested_empty",
                                    ct);
                                if (saved != null)
                                {
                                    goals = saved;
                                    // Auto-applied bootstrap: no user confirmation needed.
                                    goalSuggestions.Clear();
                                }
                            }
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

        // ------------------------------------------------------------
        // Step: DAG consensus gate for DAG mutation
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.dag_consensus"
        });

        var dagResult = await RunDagConsensusAsync(session, runId, materials, dagSnap, outputs, providerOverride, emitAssistantDelta, ct);

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
                var paperEditorOut = await RunPaperEditorAsync(
                    session,
                    runId,
                    input,
                    question,
                    materials,
                    goals,
                    dagResult,
                    outputs,
                    providerOverride,
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

        var summaryMd = await TryGetSummaryAsync(
            session.Id,
            input,
            question,
            goals,
            dagResult,
            outputs,
            goalSuggestions,
            factsWritten,
            providerOverride,
            ct);
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
}
