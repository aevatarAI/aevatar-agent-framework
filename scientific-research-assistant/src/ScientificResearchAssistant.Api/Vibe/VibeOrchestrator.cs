using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Sessions;
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

internal sealed class VibeOrchestrator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ResearchRuntime _runtime;
    private readonly MaterialsService _materials;
    private readonly WorkspaceService _workspace;
    private readonly GoalsStore _goals;
    private readonly DagStore _dag;
    private readonly DagConsensusRunner _consensus;
    private readonly TraceStore _trace;
    private readonly FileMailboxService _mailbox;
    private readonly ILogger<VibeOrchestrator> _logger;

    public VibeOrchestrator(
        ResearchRuntime runtime,
        MaterialsService materials,
        WorkspaceService workspace,
        GoalsStore goals,
        DagStore dag,
        DagConsensusRunner consensus,
        TraceStore trace,
        FileMailboxService mailbox,
        ILogger<VibeOrchestrator> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _consensus = consensus ?? throw new ArgumentNullException(nameof(consensus));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
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
        // Step: maker-v2 consensus gate for DAG mutation
        // ------------------------------------------------------------
        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.maker_v2"
        });

        var dagResult = await RunDagConsensusAsync(session, runId, dagSnap, outputs, providerOverride, emitAssistantDelta, ct);

        session.Events.Publish(new StepFinishedEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StepName = "vibe.maker_v2"
        });

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

    // ============================================================
    //  AG-UI per-agent message projection (like AxiomReasoning)
    // ============================================================

    private static void StartAgentMessage(ResearchSession session, string messageId, string agent, string stepName)
    {
        // Ensure message exists for snapshot-first reconnect.
        session.SetMessage(messageId, role: "assistant", content: string.Empty);

        session.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Role = "assistant"
        });

        // Attach metadata so frontend can label/group per agent without parsing messageId.
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.message_meta",
            Value = new { messageId, agent, stepName }
        });
    }

    private static void EmitAgentDelta(ResearchSession session, string messageId, string role, string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        session.AppendToMessage(messageId, role: role, delta);

        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Delta = delta
        });
    }

    private static void EndAgentMessage(ResearchSession session, string messageId)
    {
        session.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId
        });
    }

    // ============================================================
    //  Workers
    // ============================================================

    private async Task<string> RunPlannerAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        string? providerOverride,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
        var messageId = $"msg:{session.Id}:planner:{runId}";
        StartAgentMessage(session, messageId, agent: "planner", stepName: "vibe.planner");

        try
        {
            var (planner, plannerId) = await _runtime.GetPlannerAgentAsync(session.Id, providerOverride, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("planner", question, goals, dag, attachments: input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:planner"
            };
            req.Context["agent_id"] = plannerId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            await foreach (var chunk in planner.ChatStreamAsync(req, ct))
            {
                if (string.IsNullOrEmpty(chunk)) continue;
                sb.Append(chunk);
                EmitAgentDelta(session, messageId, "assistant", chunk);
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (Exception ex)
        {
            var msg = $"[planner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.planner" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunReasonerAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        string? plannerOutput,
        string? providerOverride,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
        var messageId = $"msg:{session.Id}:reasoner:{runId}";
        StartAgentMessage(session, messageId, agent: "reasoner", stepName: "vibe.reasoner");

        try
        {
            var (reasoner, reasonerId) = await _runtime.GetReasonerAgentAsync(session.Id, providerOverride, ct);

            // Best-effort: include python tool if enabled.
            _ = await _runtime.RefreshToolsSnapshotAsync(session.Id, providerOverride, ct);

            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("reasoner", question, goals, dag, attachments: input.AttachmentPaths,
                    extra: string.IsNullOrWhiteSpace(plannerOutput) ? null : $"Planner output (excerpt):\n{Bound(plannerOutput!, 3000)}"),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:reasoner"
            };
            req.Context["agent_id"] = reasonerId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(2048);
            var supportsStreaming = await reasoner.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await reasoner.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in reasoner.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }

            EmitAgentDelta(session, messageId, "assistant", "\n\n");
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            return Bound(sb.ToString(), 40_000);
        }
        catch (Exception ex)
        {
            var msg = $"[reasoner error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.reasoner" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunLibrarianAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        string? providerOverride,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
        var messageId = $"msg:{session.Id}:librarian:{runId}";
        StartAgentMessage(session, messageId, agent: "librarian", stepName: "vibe.librarian");

        try
        {
            var (lib, libId) = await _runtime.GetLibrarianAgentAsync(session.Id, providerOverride, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("librarian", question, goals, dag, attachments: input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:librarian"
            };
            req.Context["agent_id"] = libId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await lib.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await lib.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in lib.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (Exception ex)
        {
            var msg = $"[librarian error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.librarian" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunVerifierAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        string? reasonerOutput,
        string? providerOverride,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
        var messageId = $"msg:{session.Id}:verifier:{runId}";
        StartAgentMessage(session, messageId, agent: "verifier", stepName: "vibe.verifier");

        try
        {
            var (ver, verId) = await _runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);
            var req = new ChatRequest
            {
                Message = BuildWorkerMessage("verifier", question, goals, dag, attachments: input.AttachmentPaths,
                    extra: string.IsNullOrWhiteSpace(reasonerOutput) ? null : $"Reasoner output (excerpt):\n{Bound(reasonerOutput!, 3500)}"),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:verifier"
            };
            req.Context["agent_id"] = verId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(1024);
            var supportsStreaming = await ver.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await ver.ChatAsync(req, ct);
                var text = resp.Content ?? string.Empty;
                sb.Append(text);
                if (text.Length > 0) EmitAgentDelta(session, messageId, "assistant", text);
            }
            else
            {
                await foreach (var chunk in ver.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);
                }
            }
            EmitAgentDelta(session, messageId, "assistant", "\n\n");

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            return Bound(sb.ToString(), 20_000);
        }
        catch (Exception ex)
        {
            var msg = $"[verifier error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.verifier" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    private async Task<string> RunDagBuilderAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<LibrarianAxiomCandidate> librarianAxioms,
        string? providerOverride,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
        var messageId = $"msg:{session.Id}:dag_builder:{runId}";
        StartAgentMessage(session, messageId, agent: "dag_builder", stepName: "vibe.dag_builder");

        try
        {
            var (db, dbId) = await _runtime.GetDagBuilderAgentAsync(session.Id, providerOverride, ct);
            var req = new ChatRequest
            {
                Message = BuildDagBuilderMessage(question, goals, dag, outputs, librarianAxioms, input.AttachmentPaths),
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:dag_builder"
            };
            req.Context["agent_id"] = dbId;
            req.Context["materials_context"] = materials.RenderedContext;

            var sb = new StringBuilder(4096);
            var supportsStreaming = await db.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                var resp = await db.ChatAsync(req, ct);
                var text = (resp.Content ?? string.Empty).Trim();
                sb.Append(text);

                // Render as fenced json to UI (even if it's invalid; user can see it).
                if (text.Length > 0)
                {
                    EmitAgentDelta(session, messageId, "assistant", "```json\n");
                    EmitAgentDelta(session, messageId, "assistant", Bound(text, 20_000) + "\n");
                    EmitAgentDelta(session, messageId, "assistant", "```\n\n");
                }
            }
            else
            {
                // Stream inside fenced block for a better "always streaming" UX.
                EmitAgentDelta(session, messageId, "assistant", "```json\n");

                await foreach (var chunk in db.ChatStreamAsync(req, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    sb.Append(chunk);
                    EmitAgentDelta(session, messageId, "assistant", chunk);

                    // Keep bounded (same as return contract).
                    if (sb.Length > 30_000)
                        break;
                }

                EmitAgentDelta(session, messageId, "assistant", "\n```\n\n");
            }

            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return Bound(sb.ToString().Trim(), 30_000);
        }
        catch (Exception ex)
        {
            var msg = $"[dag_builder error] {ex.Message}\n\n";
            EmitAgentDelta(session, messageId, "assistant", msg);
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = "vibe.dag_builder" });
            return msg;
        }
        finally
        {
            EndAgentMessage(session, messageId);
        }
    }

    // ============================================================
    //  DAG consensus + persistence
    // ============================================================

    private async Task<DagRoundResult> RunDagConsensusAsync(
        ResearchSession session,
        string runId,
        SraDagSnapshot currentDag,
        IReadOnlyDictionary<string, string> outputs,
        string? providerOverride,
        Action<string> emit,
        CancellationToken ct)
    {
        // Parse candidate mutation from dag_builder output (JSON).
        var candidateText = outputs.TryGetValue("dag_builder", out var x) ? x : string.Empty;
        var candidate = TryParseDagBuilderCandidate(session.Id, candidateText);

        if (candidate == null)
        {
            EmitSection(emit, "### DAG Consensus (maker-v2)\n");
            emit("_No DAG candidate produced._\n\n");
            return new DagRoundResult(false, false, null, null, null, [], null);
        }

        DagConsensusRunner.ConsensusResult cr;
        try
        {
            cr = await _consensus.RunAsync(new DagConsensusRunner.ConsensusInput(
                SessionId: session.Id,
                RunId: runId,
                Current: currentDag,
                Candidate: candidate,
                ProviderName: providerOverride,
                ConsensusK: null,
                MaxRounds: null,
                WorkerCount: null,
                MaxDepth: null), ct);
        }
        catch (Exception ex)
        {
            EmitSection(emit, "### DAG Consensus (maker-v2)\n");
            emit($"[maker-v2 error] {ex.Message}\n\n");
            return new DagRoundResult(false, true, candidate, null, null, ["maker_v2_exception"], null);
        }

        EmitSection(emit, "### DAG Consensus (maker-v2)\n");

        if (!cr.Ok || cr.Mutation == null)
        {
            var stagedPath = await _dag.WriteStagedAsync(session.Id, candidate, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.consensus_blocked",
                Value = new
                {
                    sessionId = session.Id,
                    runId,
                    stagedPath,
                    redFlags = cr.RedFlags,
                    artifactPath = cr.ArtifactPath ?? ""
                }
            });

            emit($"**Blocked** (staged: `{stagedPath}`)\n\n");
            if (cr.RedFlags.Count > 0)
                emit($"RedFlags: {string.Join(", ", cr.RedFlags)}\n\n");

            return new DagRoundResult(false, true, candidate, null, stagedPath, cr.RedFlags, cr.ArtifactPath);
        }

        // Apply accepted mutation to snapshot.
        var applied = await _dag.ApplyMutationAsync(session.Id, cr.Mutation, ct);

        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.dag_updated",
            Value = new
            {
                sessionId = session.Id,
                runId,
                mutationId = cr.Mutation.MutationId,
                nodes = cr.Mutation.UpsertNodes.Count,
                edges = cr.Mutation.UpsertEdges.Count,
                updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
            }
        });

        emit($"**Accepted** (mutationId: `{cr.Mutation.MutationId}`)\n\n");

        return new DagRoundResult(true, false, candidate, cr.Mutation, null, [], cr.ArtifactPath);
    }

    // ============================================================
    //  Trace persistence + round summary event
    // ============================================================

    private async Task PersistTraceAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        IReadOnlyDictionary<string, string> outputs,
        DagRoundResult dagResult,
        string? summaryMarkdown,
        CancellationToken ct)
    {
        var sessionId = session.Id;
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // Best-effort monotonic round index
        var prev = await _trace.LoadLatestAsync(sessionId, max: 1, ct);
        var roundIdx = prev.Count == 0 ? 0 : prev[^1].RoundIndex + 1;

        var round = new SraRoundSummary
        {
            SessionId = sessionId,
            RunId = runId,
            RoundIndex = roundIdx,
            TriggerKind = "user_message",
            TriggerRef = (input.RequestId ?? string.Empty).Trim(),
            UpdatedAt = now
        };

        foreach (var (agent, text) in outputs)
        {
            var s = new SraRoundAgentSummary { Agent = agent };
            s.Highlights.AddRange(ExtractHighlights(text, max: 4));
            round.PerAgent.Add(s);
        }

        if (dagResult.Accepted && dagResult.AcceptedMutation != null)
        {
            foreach (var n in dagResult.AcceptedMutation.UpsertNodes)
            {
                round.DagChanges.Add(new SraRoundDagChange
                {
                    NodeId = n.Id,
                    NodeType = n.Type,
                    Change = "accepted"
                });
            }
        }
        else if (dagResult.Blocked && dagResult.StagedPath != null && dagResult.Candidate != null)
        {
            foreach (var n in dagResult.Candidate.UpsertNodes)
            {
                round.DagChanges.Add(new SraRoundDagChange
                {
                    NodeId = n.Id,
                    NodeType = n.Type,
                    Change = "staged"
                });
            }
        }

        round.Metrics["question_len"] = question.Length.ToString();
        round.Metrics["agents"] = outputs.Count.ToString();

        await _trace.AppendAsync(sessionId, round, summaryMarkdown, ct);

        // Emit a compact event for UI to extend timeline.
        try
        {
            var ws = _workspace.EnsureSessionWorkspace(sessionId);
            var summaryAbs = Path.Combine(ws.RunsDir, runId, "summary.md");
            var summaryRel = Path.GetRelativePath(ws.SessionRoot, summaryAbs).Replace('\\', '/').Trim('/');

            var preview = (summaryMarkdown ?? string.Empty).Replace("\r", "").Trim();
            if (preview.Length > 800) preview = preview[..800];

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.round_summary",
                Value = new
                {
                    sessionId,
                    runId,
                    roundIndex = roundIdx,
                    summaryPath = summaryRel,
                    preview
                }
            });
        }
        catch
        {
            // best-effort only
        }
    }

    // ============================================================
    //  research_assistant helpers
    // ============================================================

    private sealed record PlanResult(string? RawJson, List<PlanWorker>? Workers, List<GoalCandidate>? GoalsInit);
    private sealed record PlanWorker
    {
        public string? Agent { get; init; }
        public string? Task { get; init; }
    }

    private sealed record GoalCandidate
    {
        public string? GoalId { get; init; }
        public string? Text { get; init; }
        public int? Priority { get; init; }
        public string? Reason { get; init; }
    }

    private sealed record LibrarianAxiomCandidate
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public string? Citation { get; init; }
        public string? SourcePath { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private async Task<PlanResult> TryGetPlanAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildPlanMessage(question, goals, dag, recentTrace, input.ToAgents, input.AttachmentPaths);

            var req = new ChatRequest
            {
                Message = "[MODE:PLAN]\n" + msg,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:ra_plan"
            };
            req.Context["agent_id"] = raId;
            req.Context["materials_context"] = materials.RenderedContext;

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            if (!TryExtractJson(raw, out var json))
                return new PlanResult(null, null, null);

            var parsed = JsonSerializer.Deserialize<PlanJson>(json!, Json);
            var workers = parsed?.Workers?
                .Where(w => !string.IsNullOrWhiteSpace(w.Agent))
                .Select(w => new PlanWorker { Agent = w.Agent, Task = w.Task })
                .ToList();

            var goalsInit = parsed?.GoalsInit?
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Text))
                .Select(g => new GoalCandidate
                {
                    GoalId = g!.GoalId,
                    Text = g.Text,
                    Priority = g.Priority,
                    Reason = g.Reason
                })
                .ToList();

            return new PlanResult(json, workers, goalsInit);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VibeOrchestrator] research_assistant plan failed (best-effort).");
            return new PlanResult(null, null, null);
        }
    }

    private async Task<string?> TryGetSummaryAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        SraGoalsSnapshot goals,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<GoalCandidate> goalSuggestions,
        IReadOnlyList<string> factsWritten,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildSummaryMessage(question, goals, dagResult, outputs, goalSuggestions, factsWritten);

            var req = new ChatRequest
            {
                Message = "[MODE:SUMMARY]\n" + msg,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:summary"
            };
            req.Context["agent_id"] = raId;

            var resp = await ra.ChatAsync(req, ct);
            var md = (resp.Content ?? string.Empty).Replace("\r", "").Trim();
            return md.Length == 0 ? null : Bound(md, 40_000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VibeOrchestrator] research_assistant summary failed (best-effort).");
            return null;
        }
    }

    // ============================================================
    //  Parsing helpers
    // ============================================================

    private sealed class PlanJson
    {
        public string? RoundTitle { get; init; }
        public List<PlanWorkerJson>? Workers { get; init; }
        public List<GoalJson>? GoalsInit { get; init; }
        public List<string>? Notes { get; init; }
    }

    private sealed class GoalJson
    {
        public string? GoalId { get; init; }
        public string? Text { get; init; }
        public int? Priority { get; init; }
        public string? Reason { get; init; }
    }

    private sealed class PlanWorkerJson
    {
        public string? Agent { get; init; }
        public string? Task { get; init; }
        public object? Inputs { get; init; }
    }

    private static SraDagMutation? TryParseDagBuilderCandidate(string sessionId, string raw)
    {
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return null;

        DagCandidateJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DagCandidateJson>(json, Json);
        }
        catch
        {
            return null;
        }

        if (parsed == null) return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var id = (parsed.MutationId ?? string.Empty).Trim();
        if (id.Length == 0) id = $"dag_builder_{Guid.NewGuid():N}";

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = id,
            AuthorAgent = string.IsNullOrWhiteSpace(parsed.AuthorAgent) ? "dag_builder" : parsed.AuthorAgent.Trim(),
            CreatedAt = now
        };

        if (parsed.Nodes != null)
        {
            foreach (var n in parsed.Nodes)
            {
                var nid = (n?.Id ?? string.Empty).Trim();
                if (nid.Length == 0) continue;

                var node = new SraDagNode
                {
                    Id = nid,
                    Type = ParseNodeType(n!.Type),
                    Label = Bound((n.Label ?? string.Empty).Trim(), 200),
                    Proof = Bound((n.Proof ?? string.Empty).Trim(), 1200),
                    UpdatedAt = now
                };

                if (n.Tags != null)
                {
                    foreach (var kv in n.Tags)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                        node.Tags[kv.Key.Trim()] = Bound(kv.Value?.Trim() ?? "", 200);
                    }
                }

                m.UpsertNodes.Add(node);
            }
        }

        if (parsed.Edges != null)
        {
            foreach (var e in parsed.Edges)
            {
                var from = (e?.From ?? string.Empty).Trim();
                var to = (e?.To ?? string.Empty).Trim();
                if (from.Length == 0 || to.Length == 0) continue;

                m.UpsertEdges.Add(new SraDagEdge
                {
                    FromId = from,
                    ToId = to,
                    Type = string.IsNullOrWhiteSpace(e!.Type) ? "depends_on" : e.Type.Trim(),
                    UpdatedAt = now
                });
            }
        }

        return m;
    }

    private sealed class DagCandidateJson
    {
        public string? MutationId { get; init; }
        public string? AuthorAgent { get; init; }
        public List<DagNodeJson>? Nodes { get; init; }
        public List<DagEdgeJson>? Edges { get; init; }
    }

    private sealed class DagNodeJson
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Label { get; init; }
        public string? Proof { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed class DagEdgeJson
    {
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Type { get; init; }
    }

    private static SraDagNodeType ParseNodeType(string? s)
    {
        var t = (s ?? string.Empty).Trim().ToLowerInvariant();
        return t switch
        {
            "axiom" => SraDagNodeType.Axiom,
            "theorem" => SraDagNodeType.Theorem,
            "assumption" => SraDagNodeType.Assumption,
            "hypothesis" => SraDagNodeType.Hypothesis,
            "unknown" => SraDagNodeType.Unknown,
            _ => SraDagNodeType.Unknown
        };
    }

    // Best-effort JSON extraction (same spirit as tool runners).
    private static bool TryExtractJson(string stdout, out string? json)
    {
        json = null;
        if (string.IsNullOrWhiteSpace(stdout))
            return false;

        var trimmed = stdout.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            json = trimmed;
            return true;
        }

        var lastEnd = trimmed.LastIndexOf('}');
        if (lastEnd < 0)
        {
            lastEnd = trimmed.LastIndexOf(']');
            if (lastEnd < 0) return false;
        }

        for (var start = lastEnd; start >= 0; start--)
        {
            if (trimmed[start] is '{' or '[')
            {
                var candidate = trimmed.Substring(start, lastEnd - start + 1);
                if (IsValidJson(candidate))
                {
                    json = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValidJson(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    //  Librarian actions (facts write / axioms / goals)
    // ============================================================

    private sealed class LibrarianActionsJson
    {
        public List<FactWriteJson>? FactsWrite { get; init; }
        public List<AxiomForDagJson>? AxiomsForDag { get; init; }
        public List<GoalJson>? GoalSuggestions { get; init; }
    }

    private sealed class FactWriteJson
    {
        public string? Title { get; init; }
        public string? RelativePath { get; init; }
        public string? Content { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed class AxiomForDagJson
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public string? Citation { get; init; }
        public string? SourcePath { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed record LibrarianFactWrite(string Title, string Content, string? RelativePath, Dictionary<string, string?>? Tags);

    private sealed record LibrarianActions(
        List<LibrarianFactWrite> FactsWrite,
        List<LibrarianAxiomCandidate> AxiomsForDag,
        List<GoalCandidate> GoalSuggestions);

    private static LibrarianActions? TryParseLibrarianActions(string raw)
    {
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return null;

        LibrarianActionsJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LibrarianActionsJson>(json!, Json);
        }
        catch
        {
            return null;
        }

        if (parsed == null)
            return null;

        var facts = new List<LibrarianFactWrite>();
        if (parsed.FactsWrite is { Count: > 0 })
        {
            foreach (var f in parsed.FactsWrite)
            {
                if (f == null) continue;
                var content = (f.Content ?? string.Empty).Replace("\r", "").Trim();
                if (content.Length == 0) continue;

                var title = (f.Title ?? string.Empty).Trim();
                if (title.Length == 0) title = "fact";

                facts.Add(new LibrarianFactWrite(title, content, f.RelativePath, f.Tags));
                if (facts.Count >= 8) break; // bound
            }
        }

        var axioms = new List<LibrarianAxiomCandidate>();
        if (parsed.AxiomsForDag is { Count: > 0 })
        {
            foreach (var a in parsed.AxiomsForDag)
            {
                if (a == null) continue;
                var id = (a.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                var label = (a.Label ?? string.Empty).Replace("\r", "").Trim();
                var citation = (a.Citation ?? string.Empty).Replace("\r", "").Trim();

                axioms.Add(new LibrarianAxiomCandidate
                {
                    Id = id,
                    Label = Bound(label, 220),
                    Citation = Bound(citation, 400),
                    SourcePath = Bound((a.SourcePath ?? string.Empty).Trim(), 240),
                    Tags = a.Tags
                });

                if (axioms.Count >= 20) break;
            }
        }

        var goals = new List<GoalCandidate>();
        if (parsed.GoalSuggestions is { Count: > 0 })
        {
            foreach (var g in parsed.GoalSuggestions)
            {
                if (g == null) continue;
                var text = (g.Text ?? string.Empty).Replace("\r", "").Trim();
                if (text.Length == 0) continue;

                goals.Add(new GoalCandidate
                {
                    GoalId = g.GoalId,
                    Text = Bound(text, 2000),
                    Priority = g.Priority,
                    Reason = g.Reason
                });
                if (goals.Count >= 30) break;
            }
        }

        if (facts.Count == 0 && axioms.Count == 0 && goals.Count == 0)
            return null;

        return new LibrarianActions(facts, axioms, goals);
    }

    private async Task<List<string>> TryWriteFactsAsync(string sessionId, IReadOnlyList<LibrarianFactWrite> facts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (facts.Count == 0) return [];

        var written = new List<string>(capacity: Math.Min(4, facts.Count));
        var max = Math.Clamp(facts.Count, 0, 3);

        for (var i = 0; i < max; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = facts[i];

            try
            {
                var rel = BuildSessionScopedFactPath(sessionId, f.RelativePath, f.Title);

                // If tags exist, prepend a small "meta" header to content (human-readable).
                var content = f.Content;
                if (f.Tags is { Count: > 0 })
                {
                    var kvs = f.Tags
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        .Take(20)
                        .Select(kv => $"- {kv.Key.Trim()}: {kv.Value!.Trim()}");
                    var meta = string.Join('\n', kvs);
                    if (!string.IsNullOrWhiteSpace(meta))
                    {
                        content = $"Meta:\n{meta}\n\n{content}";
                    }
                }

                var saved = await _materials.SaveFactAsync(f.Title, content, rel, ct);
                written.Add(saved.Id);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[VibeOrchestrator] librarian fact write failed (best-effort).");
            }
        }

        return written;
    }

    private static string BuildSessionScopedFactPath(string sessionId, string? proposedRelativePath, string title)
    {
        // Always sandbox under sra/{sessionId}/...
        var prefix = $"sra/{sessionId}/";
        var rel = (proposedRelativePath ?? string.Empty).Replace('\\', '/').Trim();
        if (rel.StartsWith("/", StringComparison.Ordinal))
            rel = rel.TrimStart('/');

        if (rel.Length > 0)
        {
            // Remove traversal segments.
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p != "." && p != "..")
                .ToList();
            rel = string.Join('/', parts);
        }

        if (rel.Length == 0)
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            rel = $"facts/{stamp}_{Slugify(title, 48)}.md";
        }

        // Enforce extension.
        var ext = Path.GetExtension(rel);
        if (string.IsNullOrWhiteSpace(ext))
            rel += ".md";

        // Ensure prefix once.
        if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rel = prefix + rel.TrimStart('/');

        return rel;
    }

    private static string Slugify(string input, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 8, 96);
        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0) return "note";

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 64));
        var prevDash = false;
        foreach (var ch in s)
        {
            if (sb.Length >= maxChars) break;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var outSlug = sb.ToString().Trim('-').Trim();
        return outSlug.Length == 0 ? "note" : outSlug;
    }

    // ============================================================
    //  Goals persistence helper (same behavior as Goals API)
    // ============================================================

    private async Task<SraGoalsSnapshot?> TrySaveGoalsAsync(
        ResearchSession session,
        SraGoalsSnapshot existing,
        IReadOnlyList<GoalCandidate> candidates,
        string updatedBy,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var targetVersion = Math.Max(existing.Version + 1, 1);

            var snap = new SraGoalsSnapshot
            {
                SessionId = session.Id,
                Version = targetVersion
            };

            var idx = 0;
            foreach (var g in candidates)
            {
                if (g == null) continue;
                var text = (g.Text ?? string.Empty).Replace("\r", "").Trim();
                if (text.Length == 0) continue;

                idx++;
                var goalId = (g.GoalId ?? string.Empty).Trim();
                if (goalId.Length == 0)
                    goalId = $"g{idx}";

                snap.Goals.Add(new SraGoalItem
                {
                    GoalId = goalId,
                    Text = text,
                    Priority = g.Priority ?? 0
                });

                if (snap.Goals.Count >= 50) break; // bound
            }

            if (snap.Goals.Count == 0)
                return null;

            var saved = await _goals.SaveAsync(session.Id, snap, ct);

            // UI: notify goals updated (best-effort)
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.goals_updated",
                Value = new { sessionId = session.Id, version = saved.Version, count = saved.Goals.Count }
            });

            // Mailbox broadcast (best-effort) - stable roster for MVP.
            try
            {
                var now = Timestamp.FromDateTime(DateTime.UtcNow);
                var evt = new SraGoalsUpdated
                {
                    SessionId = session.Id,
                    Snapshot = saved,
                    Reason = reason ?? "auto",
                    UpdatedBy = updatedBy ?? "system",
                    CreatedAt = now
                };

                var toAgents = new[] { "research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder" };
                foreach (var a in toAgents)
                {
                    var envelope = new SraMailboxMessage
                    {
                        SessionId = session.Id,
                        MessageId = $"goals_updated:{saved.Version}",
                        FromAgent = updatedBy ?? "system",
                        ToAgent = a,
                        Type = "goals.updated",
                        CorrelationId = $"goals:{saved.Version}",
                        CreatedAt = now,
                        Payload = Any.Pack(evt)
                    };

                    await _mailbox.SendAsync(session.Id, a, envelope, ct);
                }
            }
            catch
            {
                // best-effort only
            }

            return saved;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VibeOrchestrator] SaveGoals failed (best-effort).");
            return null;
        }
    }

    private static int WorkerOrder(string agent)
    {
        return agent switch
        {
            "planner" => 0,
            "reasoner" => 1,
            "librarian" => 2,
            "verifier" => 3,
            "dag_builder" => 4,
            _ => 99
        };
    }

    // ============================================================
    //  Formatting helpers (messages)
    // ============================================================

    private static string BuildPlanMessage(
        string question,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> trace,
        List<string>? toAgents,
        List<string>? attachmentPaths)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"Question: {question}");

        if (toAgents is { Count: > 0 })
            sb.AppendLine($"RoutingHint.ToAgents: [{string.Join(", ", toAgents.Select(x => x.Trim()).Where(x => x.Length > 0))}]");
        if (attachmentPaths is { Count: > 0 })
            sb.AppendLine($"AttachmentPaths: [{string.Join(", ", attachmentPaths.Select(x => x.Trim()).Where(x => x.Length > 0))}]");

        sb.AppendLine();
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).ThenBy(x => x.GoalId, StringComparer.Ordinal).Take(20))
        {
            sb.AppendLine($"- ({g.Priority}) {g.GoalId}: {Bound(g.Text ?? "", 200)}");
        }

        sb.AppendLine();
        sb.AppendLine("DagStats:");
        sb.AppendLine($"- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={(dag.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "")}");

        if (trace.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RecentTrace (titles/excerpts):");
            foreach (var t in trace.TakeLast(3))
            {
                sb.AppendLine($"- round={t.RoundIndex}, run={t.RunId}, agents={t.PerAgent.Count}, dagChanges={t.DagChanges.Count}");
            }
        }

        return sb.ToString();
    }

    private static string BuildSummaryMessage(
        string question,
        SraGoalsSnapshot goals,
        DagRoundResult dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<GoalCandidate> goalSuggestions,
        IReadOnlyList<string> factsWritten)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine($"Question: {question}");
        sb.AppendLine();
        sb.AppendLine("Goals (top):");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(10))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

        if (factsWritten is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Facts written this round (ids/paths):");
            foreach (var p in factsWritten.Take(10))
                sb.AppendLine($"- {Bound(p ?? string.Empty, 240)}");
        }

        if (goalSuggestions is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Goal suggestions (NEED USER CONFIRMATION; not yet applied):");
            foreach (var g in goalSuggestions
                         .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                         .OrderBy(x => x.Priority ?? 0)
                         .Take(12))
            {
                var reason = string.IsNullOrWhiteSpace(g.Reason) ? "" : $" (reason: {Bound(g.Reason!, 160)})";
                sb.AppendLine($"- ({g.Priority ?? 0}) {Bound(g.Text ?? string.Empty, 240)}{reason}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("DAG outcome:");
        if (dag.Accepted && dag.AcceptedMutation != null)
            sb.AppendLine($"- accepted mutationId={dag.AcceptedMutation.MutationId} nodes={dag.AcceptedMutation.UpsertNodes.Count} edges={dag.AcceptedMutation.UpsertEdges.Count}");
        else if (dag.Blocked)
            sb.AppendLine($"- blocked redFlags=[{string.Join(", ", dag.RedFlags)}] staged={dag.StagedPath ?? ""}");
        else
            sb.AppendLine("- no candidate");

        sb.AppendLine();
        sb.AppendLine("Worker outputs (excerpts):");
        foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{k}]");
            sb.AppendLine(Bound(v ?? "", 2500));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildWorkerMessage(
        string role,
        string question,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        List<string>? attachments,
        string? extra = null)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"Role: {role}");
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(12))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

        sb.AppendLine();
        sb.AppendLine("DAG stats:");
        sb.AppendLine($"- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}");

        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine();
            sb.AppendLine(extra.Trim());
        }

        return sb.ToString();
    }

    private static string BuildDagBuilderMessage(
        string question,
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<LibrarianAxiomCandidate> librarianAxioms,
        List<string>? attachments)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("You must output STRICT JSON ONLY.");
        sb.AppendLine();
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(12))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

        sb.AppendLine();
        sb.AppendLine($"Current DAG: nodes={dag.Nodes.Count}, edges={dag.Edges.Count}");

        if (librarianAxioms is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Librarian trusted axioms (include as AXIOM nodes when appropriate):");
            try
            {
                // Provide a deterministic, bounded JSON snippet to the model.
                var arr = librarianAxioms
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                    .Take(12)
                    .Select(a => new
                    {
                        id = (a!.Id ?? string.Empty).Trim(),
                        label = Bound(a.Label ?? string.Empty, 200),
                        citation = Bound(a.Citation ?? string.Empty, 300),
                        sourcePath = Bound(a.SourcePath ?? string.Empty, 200),
                        tags = a.Tags != null
                            ? a.Tags.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value!.Trim(), StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>()
                    })
                    .ToList();

                sb.AppendLine(JsonSerializer.Serialize(arr, Json));
            }
            catch
            {
                // best-effort only
            }
        }

        sb.AppendLine();
        sb.AppendLine("Worker outputs (excerpts):");
        foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(k, "dag_builder", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine($"[{k}]");
            sb.AppendLine(Bound(v ?? "", 2200));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> ExtractHighlights(string text, int max)
    {
        max = Math.Clamp(max, 0, 10);
        if (max == 0) return [];

        var lines = (text ?? string.Empty)
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return lines.Take(max).ToList();
    }

    private static void EmitSection(Action<string> emit, string header)
    {
        emit(header);
        if (!header.EndsWith("\n", StringComparison.Ordinal))
            emit("\n");
    }

    private sealed record DagRoundResult(
        bool Accepted,
        bool Blocked,
        SraDagMutation? Candidate,
        SraDagMutation? AcceptedMutation,
        string? StagedPath,
        IReadOnlyList<string> RedFlags,
        string? ConsensusArtifactPath);

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Bound(string s, int max)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= max) return t;
        return t[..max];
    }
}


