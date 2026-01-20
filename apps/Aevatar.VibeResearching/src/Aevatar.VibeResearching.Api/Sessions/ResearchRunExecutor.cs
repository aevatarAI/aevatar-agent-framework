using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Cognitive.Streaming;
using Microsoft.Extensions.Options;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Workspace;
using VibeResearching.Streaming;

namespace VibeResearching.Api.Sessions;

// ============================================================
//  ResearchRunExecutor
//
//  Purpose:
//  - Execute a single "run" for a session (chat or vibe-researching).
//  - Project progress into AG-UI event stream:
//      RUN/STEP/TEXT + (optional) STATE + CUSTOM tool events
//
//  Notes:
//  - All output must be best-effort: never crash the server due to UI projection.
//  - Runs are serialized per session to avoid history/tool-loop corruption.
// ============================================================

internal sealed class ResearchRunExecutor
{
    private readonly ResearchRuntime _runtime;
    private readonly MaterialsService _materials;
    private readonly WorkspaceService _workspace;
    private readonly VibeOrchestrator _vibe;
    private readonly VibeGoalLoopRunner _vibeLoop;
    private readonly VibeMilestoneLoopRunner _milestoneLoop;
    private readonly AgentProvidersStore _agentProviders;
    private readonly IOptions<LLMProvidersConfig> _llm;
    private readonly ILogger<ResearchRunExecutor> _logger;

    public ResearchRunExecutor(
        ResearchRuntime runtime,
        MaterialsService materials,
        WorkspaceService workspace,
        VibeOrchestrator vibe,
        VibeGoalLoopRunner vibeLoop,
        VibeMilestoneLoopRunner milestoneLoop,
        AgentProvidersStore agentProviders,
        IOptions<LLMProvidersConfig> llm,
        ILogger<ResearchRunExecutor> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _vibe = vibe ?? throw new ArgumentNullException(nameof(vibe));
        _vibeLoop = vibeLoop ?? throw new ArgumentNullException(nameof(vibeLoop));
        _milestoneLoop = milestoneLoop ?? throw new ArgumentNullException(nameof(milestoneLoop));
        _agentProviders = agentProviders ?? throw new ArgumentNullException(nameof(agentProviders));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);

        // Default mode changed from "chat" to "milestone" for research-driven workflow.
        // Milestone mode executes research by iterating through plan milestones.
        var mode = (input.Mode ?? "milestone").Trim().ToLowerInvariant();

        // Milestone-driven research: execute by iterating through milestones in order
        // "vibe" now defaults to milestone loop for full research workflow
        if (mode is "milestone" or "vibe_milestone" or "research" or "vibe")
        {
            await ExecuteMilestoneLoopRunAsync(session, runId, input, ct);
            return;
        }
        // Legacy: iteration-based loop (not milestone-aware)
        if (mode is "vibe_loop" or "vibe_goal_loop")
        {
            await ExecuteVibeGoalLoopRunAsync(session, runId, input, ct);
            return;
        }
        // Single-round research (for debugging or quick tests)
        if (mode is "vibe_researching" or "axiom" or "single")
        {
            await ExecuteVibeResearchingRunAsync(session, runId, input, ct);
            return;
        }
        // Pure chat mode (no research orchestration)
        if (mode is "chat")
        {
            await ExecuteChatRunAsync(session, runId, input, ct);
            return;
        }

        // Unknown mode: default to milestone-driven research
        _logger.LogWarning("Unknown mode '{Mode}', defaulting to milestone-driven research", mode);
        await ExecuteMilestoneLoopRunAsync(session, runId, input, ct);
    }

    private async Task ExecuteVibeGoalLoopRunAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        CancellationToken ct)
    {
        string? error = null;
        var assistant = new StringBuilder(capacity: 2048);
        var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
        var assistantMessageStarted = false;
        var assistantMessageEnded = false;
        var lockHeld = false;

        try
        {
            await session.RunLock.WaitAsync(ct);
            lockHeld = true;

            // NOTE:
            // - Default is per-agent providers (Agents panel config).
            // - Only use ProviderName when the caller explicitly overrides it.
            var providerOverride = string.IsNullOrWhiteSpace(input.ProviderName)
                ? null
                : input.ProviderName.Trim();
            var question = (input.Message ?? string.Empty).Trim();

            session.Events.Publish(new RunStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId
            });

            // Emit user message
            var userMessageId = $"msg:{session.Id}:user:{runId}";
            EmitUserMessage(session, userMessageId, question);

            // One assistant message stream; multi-agent outputs are merged with clear section headers.
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });
            assistantMessageStarted = true;

            // Bind tool progress events to this run (works for planner/reasoner tools as well).
            var prevSink = ResearchStreamEventContext.Current;
            ResearchStreamEventContext.Current = new AgUiResearchStreamEventSink(
                _runtime,
                sessionId: session.Id,
                hub: session.Events,
                threadId: session.Id,
                runId: runId);

            try
            {
                // ------------------------------------------------------------
                // Step 1) Materials (once per loop run)
                // ------------------------------------------------------------
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                var snapshot = await _materials.LoadAsync(session.Id, session.EffectiveDagId, query: question, ct);
                HydrateWorkspace(session, runId, question, snapshot);
                WorkspaceProjection.ApplyKnowledge(session.Workspace, _workspace.ScanWorkspace(session.Id), snapshot);

                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                // ------------------------------------------------------------
                // Step 2+) Outer loop (multiple rounds)
                // ------------------------------------------------------------
                void Emit(string delta)
                {
                    if (string.IsNullOrEmpty(delta)) return;
                    assistant.Append(delta);
                    EmitAssistantDelta(session, assistantMessageId, delta);
                }

                var loopResult = await _vibeLoop.ExecuteUntilGoalAsync(
                    session,
                    runId,
                    input,
                    question,
                    snapshot,
                    providerOverride,
                    Emit,
                    ct);

                // ------------------------------------------------------------
                // Done
                // ------------------------------------------------------------
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
                assistantMessageEnded = true;

                session.Events.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = session.Id,
                    RunId = runId,
                    Result = new
                    {
                        ok = true,
                        assistantMessageId,
                        assistant = assistant.ToString(),
                        mode = "vibe_loop",
                        loop = loopResult
                    }
                });
            }
            finally
            {
                ResearchStreamEventContext.Current = prevSink;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "run canceled";

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.run_canceled",
                Value = new { threadId = session.Id, runId }
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, canceled = true, error, mode = "vibe_loop" }
            });

            _logger.LogInformation("[Scientific] Vibe loop run canceled: {RunId}", runId);
        }
        catch (Exception ex)
        {
            error = ex.Message;

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = error,
                Code = "SRA_VIBE_LOOP_RUN_ERROR"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error, mode = "vibe_loop" }
            });

            _logger.LogError(ex, "[Scientific] Vibe loop run failed: {Message}", ex.Message);
        }
        finally
        {
            if (assistantMessageStarted && !assistantMessageEnded)
            {
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
            }
            if (lockHeld)
        {
            session.RunLock.Release();
            }
        }
    }

    private async Task ExecuteMilestoneLoopRunAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        CancellationToken ct)
    {
        string? error = null;
        var assistant = new StringBuilder(capacity: 4096);
        var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
        var assistantMessageStarted = false;
        var assistantMessageEnded = false;
        var lockHeld = false;

        try
        {
            await session.RunLock.WaitAsync(ct);
            lockHeld = true;

            var providerOverride = string.IsNullOrWhiteSpace(input.ProviderName)
                ? null
                : input.ProviderName.Trim();
            var question = (input.Message ?? string.Empty).Trim();

            session.Events.Publish(new RunStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId
            });

            // Emit user message
            var userMessageId = $"msg:{session.Id}:user:{runId}";
            EmitUserMessage(session, userMessageId, question);

            // One assistant message stream; multi-agent outputs are merged with clear section headers.
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });
            assistantMessageStarted = true;

            // Bind tool progress events to this run.
            var prevSink = ResearchStreamEventContext.Current;
            ResearchStreamEventContext.Current = new AgUiResearchStreamEventSink(
                _runtime,
                sessionId: session.Id,
                hub: session.Events,
                threadId: session.Id,
                runId: runId);

            try
            {
                // ------------------------------------------------------------
                // Step 1) Materials (once per milestone run)
                // ------------------------------------------------------------
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                var snapshot = await _materials.LoadAsync(session.Id, session.EffectiveDagId, query: question, ct);
                HydrateWorkspace(session, runId, question, snapshot);
                WorkspaceProjection.ApplyKnowledge(session.Workspace, _workspace.ScanWorkspace(session.Id), snapshot);

                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                // ------------------------------------------------------------
                // Step 2+) Milestone-driven loop (execute each milestone in order)
                // ------------------------------------------------------------
                void Emit(string delta)
                {
                    if (string.IsNullOrEmpty(delta)) return;
                    assistant.Append(delta);
                    EmitAssistantDelta(session, assistantMessageId, delta);
                }

                var loopResult = await _milestoneLoop.ExecuteByMilestonesAsync(
                    session,
                    runId,
                    input,
                    question,
                    snapshot,
                    providerOverride,
                    Emit,
                    ct);

                // ------------------------------------------------------------
                // Done
                // ------------------------------------------------------------
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
                assistantMessageEnded = true;

                session.Events.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = session.Id,
                    RunId = runId,
                    Result = new
                    {
                        ok = loopResult.Ok,
                        assistantMessageId,
                        assistant = assistant.ToString(),
                        mode = "milestone",
                        milestones = new
                        {
                            executed = loopResult.MilestonesExecuted,
                            total = loopResult.TotalMilestones,
                            stopReason = loopResult.StopReason
                        }
                    }
                });
            }
            finally
            {
                ResearchStreamEventContext.Current = prevSink;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "run canceled";

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.run_canceled",
                Value = new { threadId = session.Id, runId }
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, canceled = true, error, mode = "milestone" }
            });

            _logger.LogInformation("[Scientific] Milestone loop run canceled: {RunId}", runId);
        }
        catch (Exception ex)
        {
            error = ex.Message;

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = error,
                Code = "SRA_MILESTONE_LOOP_RUN_ERROR"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error, mode = "milestone" }
            });

            _logger.LogError(ex, "[Scientific] Milestone loop run failed: {Message}", ex.Message);
        }
        finally
        {
            if (assistantMessageStarted && !assistantMessageEnded)
            {
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
            }
            if (lockHeld)
            {
                session.RunLock.Release();
            }
        }
    }

    private async Task ExecuteChatRunAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        CancellationToken ct)
    {
        ChatResponse? resp = null;
        string? error = null;

        // Streamed content buffer (for run finished result).
        var assistant = new StringBuilder(capacity: 1024);
        var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
        var assistantMessageStarted = false;
        var assistantMessageEnded = false;
        var lockHeld = false;

        // Serialize runs per session.
        try
        {
            await session.RunLock.WaitAsync(ct);
            lockHeld = true;

            // NOTE:
            // - Default is per-agent providers (Agents panel config).
            // - Only use ProviderName when the caller explicitly overrides it.
            var providerOverride = string.IsNullOrWhiteSpace(input.ProviderName)
                ? null
                : input.ProviderName.Trim();

            // If no override was provided, prefer the research_assistant mapping so "chat" can follow the same config.
            if (string.IsNullOrWhiteSpace(providerOverride))
            {
                try
                {
                    var snap = await _agentProviders.LoadAsync(session.Id, ct);
                    if (snap.Map.TryGetValue("research_assistant", out var p) && !string.IsNullOrWhiteSpace(p))
                        providerOverride = p.Trim();
                }
                catch
                {
                    // best-effort only
                }
            }

            // Always emit RUN_STARTED first
            session.Events.Publish(new RunStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId
            });

            session.Events.Publish(new StepStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StepName = "chat"
            });

            // Emit user message
            var userMessageId = $"msg:{session.Id}:user:{runId}";
            EmitUserMessage(session, userMessageId, (input.Message ?? string.Empty).Trim());

            // Emit assistant message stream
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });
            assistantMessageStarted = true;

            // Initialize agent AFTER we've notified UI that the run started (avoids "blank" UI when init is slow).
            var (agent, agentId) = await _runtime.GetAgentAsync(session.Id, providerOverride, ct);

            // Best-effort: pre-load tools so MCP tool names are known (for UI tags).
            _ = await _runtime.GetToolsSnapshotAsync(session.Id, providerOverride, ct);

            // Bind tool progress events to AG-UI stream (best-effort).
            var prevSink = ResearchStreamEventContext.Current;
            ResearchStreamEventContext.Current = new AgUiResearchStreamEventSink(
                _runtime,
                sessionId: session.Id,
                hub: session.Events,
                threadId: session.Id,
                runId: runId);

            try
            {
                var requestId = input.RequestId ?? Guid.NewGuid().ToString("N");
                var request = new ChatRequest
                {
                    Message = (input.Message ?? string.Empty).Trim(),
                    RequestId = requestId,
                    StageHint = "session:chat"
                };

                // Helpful metadata for debugging / tracing
                request.Context["agent_id"] = agentId;
                request.Context["llm_default"] = _llm.Value.Default ?? "";

                var supportsStreaming = await agent.SupportsStreamingAsync(ct);
                if (!supportsStreaming)
                {
                    resp = await agent.ChatAsync(request, ct);
                    var text = resp.Content ?? string.Empty;
                    if (text.Length > 0)
                    {
                        assistant.Append(text);
                        EmitAssistantDelta(session, assistantMessageId, text);
                    }
                }
                else
                {
                    await foreach (var chunk in agent.ChatStreamAsync(request, ct))
                    {
                        if (string.IsNullOrEmpty(chunk))
                            continue;

                        assistant.Append(chunk);
                        EmitAssistantDelta(session, assistantMessageId, chunk);
                    }
                }
            }
            finally
            {
                ResearchStreamEventContext.Current = prevSink;
            }

            session.Events.Publish(new TextMessageEndEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId
            });
            assistantMessageEnded = true;

            session.Events.Publish(new StepFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StepName = "chat"
            });

            // Synthesized response for run result.
            var assistantText = assistant.ToString();
            resp ??= new ChatResponse { Content = assistantText, RequestId = input.RequestId ?? "" };

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = true, assistantMessageId, assistant = assistantText }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "run canceled";

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.run_canceled",
                Value = new { threadId = session.Id, runId }
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, canceled = true, error, mode = "chat" }
            });

            _logger.LogInformation("[Scientific] Session run canceled: {RunId}", runId);
        }
        catch (Exception ex)
        {
            error = ex.Message;

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = error,
                Code = "SCIENTIFIC_RUN_ERROR"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error }
            });

            _logger.LogError(ex, "[Scientific] Session run failed: {Message}", ex.Message);
        }
        finally
        {
            if (assistantMessageStarted && !assistantMessageEnded)
            {
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
            }
            if (lockHeld)
        {
            session.RunLock.Release();
            }
        }
    }

    private async Task ExecuteVibeResearchingRunAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        CancellationToken ct)
    {
        string? error = null;
        var assistant = new StringBuilder(capacity: 2048);
        var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
        var assistantMessageStarted = false;
        var assistantMessageEnded = false;
        var lockHeld = false;

        try
        {
            await session.RunLock.WaitAsync(ct);
            lockHeld = true;

            var providerOverride = string.IsNullOrWhiteSpace(input.ProviderName)
                ? session.ProviderName
                : input.ProviderName.Trim();
            var question = (input.Message ?? string.Empty).Trim();

            session.Events.Publish(new RunStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId
            });

            // Emit user message
            var userMessageId = $"msg:{session.Id}:user:{runId}";
            EmitUserMessage(session, userMessageId, question);

            // One assistant message stream; multi-agent outputs are merged with clear section headers.
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });
            assistantMessageStarted = true;

            // Bind tool progress events to this run (works for planner/reasoner tools as well).
            var prevSink = ResearchStreamEventContext.Current;
            ResearchStreamEventContext.Current = new AgUiResearchStreamEventSink(
                _runtime,
                sessionId: session.Id,
                hub: session.Events,
                threadId: session.Id,
                runId: runId);

            try
            {
                // ------------------------------------------------------------
                // Step 1) Materials
                // ------------------------------------------------------------
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                var snapshot = await _materials.LoadAsync(session.Id, session.EffectiveDagId, query: question, ct);
                HydrateWorkspace(session, runId, question, snapshot);
                WorkspaceProjection.ApplyKnowledge(session.Workspace, _workspace.ScanWorkspace(session.Id), snapshot);

                session.Events.Publish(new StateSnapshotEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Snapshot = session.Workspace
                });

                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.materials"
                });

                // ------------------------------------------------------------
                // Step 2+) Orchestrator (research_assistant + workers + DAG consensus + trace)
                // ------------------------------------------------------------
                void Emit(string delta)
                {
                    if (string.IsNullOrEmpty(delta)) return;
                    assistant.Append(delta);
                    EmitAssistantDelta(session, assistantMessageId, delta);
                }

                await _vibe.ExecuteOneRoundAsync(
                    session,
                    runId,
                    input,
                    question,
                    snapshot,
                    providerOverride,
                    Emit,
                    ct);

                // ------------------------------------------------------------
                // Done
                // ------------------------------------------------------------
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
                assistantMessageEnded = true;

                session.Events.Publish(new RunFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ThreadId = session.Id,
                    RunId = runId,
                    Result = new
                    {
                        ok = true,
                        assistantMessageId,
                        assistant = assistant.ToString(),
                        mode = "vibe"
                    }
                });
            }
            finally
            {
                ResearchStreamEventContext.Current = prevSink;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error = "run canceled";

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.run_canceled",
                Value = new { threadId = session.Id, runId }
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, canceled = true, error, mode = "vibe" }
            });

            _logger.LogInformation("[Scientific] Vibe run canceled: {RunId}", runId);
        }
        catch (Exception ex)
        {
            error = ex.Message;

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = error,
                Code = "SRA_VIBE_RUN_ERROR"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error, mode = "vibe" }
            });

            _logger.LogError(ex, "[Scientific] Vibe run failed: {Message}", ex.Message);
        }
        finally
        {
            if (assistantMessageStarted && !assistantMessageEnded)
            {
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });
            }
            if (lockHeld)
        {
            session.RunLock.Release();
            }
        }
    }

    private static void EmitUserMessage(ResearchSession session, string messageId, string content)
    {
        session.SetMessage(messageId, role: "user", content);

        session.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Role = "user"
        });
        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Delta = content
        });
        session.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId
        });
    }

    private static void EmitAssistantDelta(ResearchSession session, string messageId, string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        session.AppendToMessage(messageId, role: "assistant", delta);

        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Delta = delta
        });
    }

    private static void HydrateWorkspace(
        ResearchSession session,
        string runId,
        string question,
        MaterialsSnapshot snapshot)
    {
        var ws = session.Workspace;
        WorkspaceProjection.ApplyMaterials(ws, snapshot);

        ws.Vibe.LastRunId = runId;
        ws.Vibe.LastGoal = question;
        ws.Vibe.Steps =
        [
            "vibe.materials",
            "vibe.ra_plan",
            "vibe.planner",
            "vibe.reasoner",
            "vibe.librarian",
            "vibe.verifier",
            "vibe.dag_builder",
            "vibe.dag_consensus",
            "vibe.summary"
        ];
    }

    // (Workspace projection helpers are centralized in WorkspaceProjection.)

    // ============================================================
    //  Tool progress → AG-UI projection (CUSTOM events)
    // ============================================================
    private sealed class AgUiResearchStreamEventSink : IResearchStreamEventSink
    {
        private readonly ResearchRuntime _runtime;
        private readonly string _sessionId;
        private readonly BroadcastEventHub<AgUiEvent> _hub;
        private readonly string _threadId;
        private readonly string _runId;

        public AgUiResearchStreamEventSink(
            ResearchRuntime runtime,
            string sessionId,
            BroadcastEventHub<AgUiEvent> hub,
            string threadId,
            string runId)
        {
            _runtime = runtime;
            _sessionId = sessionId;
            _hub = hub;
            _threadId = threadId;
            _runId = runId;
        }

        public async Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct)
        {
            var isMcp = await _runtime.IsMcpToolAsync(_sessionId, toolName, ct);
            // Frontend expects tools to be attached to the assistant message of the current run
            var messageId = $"msg:{_threadId}:assistant:{_runId}";

            PublishTraceEvents(BuildToolTraceEvent(
                phase: ExecutionTraceEventPhase.ToolStart,
                status: ExecutionTraceEventStatus.Running,
                toolCallId: toolCallId,
                toolName: toolName,
                messageId: messageId,
                message: $"tool.start:{toolName}"));

            _hub.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.tool_start",
                Value = new { threadId = _threadId, runId = _runId, toolCallId, toolName, isMcp }
            });
        }

        public Task EmitToolProgressAsync(string toolCallId, string toolName, string message, CancellationToken ct)
        {
            // NOTE:
            // - Emit ExecutionTraceEvent and project to AG-UI (best-effort).
            var messageId = $"msg:{_threadId}:assistant:{_runId}";
            var payload = (message ?? string.Empty).Replace("\r", "").Trim();
            if (payload.Length > 2000) payload = payload[..2000];

            PublishTraceEvents(BuildToolTraceEvent(
                phase: ExecutionTraceEventPhase.ToolProgress,
                status: ExecutionTraceEventStatus.Running,
                toolCallId: toolCallId,
                toolName: toolName,
                messageId: messageId,
                message: payload));

            return Task.CompletedTask;
        }

        public async Task EmitToolEndAsync(
            string toolCallId,
            string toolName,
            bool success,
            long durationMs,
            string? error,
            string? resultPreview,
            CancellationToken ct)
        {
            var isMcp = await _runtime.IsMcpToolAsync(_sessionId, toolName, ct);
            var messageId = $"msg:{_threadId}:assistant:{_runId}";

            PublishTraceEvents(BuildToolTraceEvent(
                phase: ExecutionTraceEventPhase.ToolEnd,
                status: success ? ExecutionTraceEventStatus.Completed : ExecutionTraceEventStatus.Failed,
                toolCallId: toolCallId,
                toolName: toolName,
                messageId: messageId,
                message: resultPreview ?? (error ?? string.Empty),
                durationMs: durationMs,
                error: error));

            _hub.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.tool_end",
                Value = new
                {
                    threadId = _threadId,
                    runId = _runId,
                    toolCallId,
                    toolName,
                    isMcp,
                    success,
                    durationMs,
                    error,
                    resultPreview
                }
            });
        }

        private void PublishTraceEvents(ExecutionTraceEvent traceEvent)
        {
            var mapped = AgUiTraceProjector.Map(traceEvent);
            for (var i = 0; i < mapped.Count; i++)
            {
                _hub.Publish(mapped[i]);
            }
        }

        private ExecutionTraceEvent BuildToolTraceEvent(
            string phase,
            string status,
            string toolCallId,
            string toolName,
            string messageId,
            string message,
            long? durationMs = null,
            string? error = null)
        {
            var evt = new ExecutionTraceEvent
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Phase = phase,
                Message = message ?? string.Empty,
                NodeId = $"tool:{toolCallId}"
            };

            evt.Fields[ExecutionTraceEventFields.Status] =
                ExecutionTraceEventFieldValue.FromString(status);
            evt.Fields[ExecutionTraceEventFields.SessionId] =
                ExecutionTraceEventFieldValue.FromString(_sessionId);
            evt.Fields[ExecutionTraceEventFields.ExecutionId] =
                ExecutionTraceEventFieldValue.FromString(_runId);
            evt.Fields[ExecutionTraceEventFields.ToolName] =
                ExecutionTraceEventFieldValue.FromString(toolName);
            evt.Fields[ExecutionTraceEventFields.ToolCallId] =
                ExecutionTraceEventFieldValue.FromString(toolCallId);
            evt.Fields[ExecutionTraceEventFields.MessageId] =
                ExecutionTraceEventFieldValue.FromString(messageId);
            evt.Fields[ExecutionTraceEventFields.Phase] =
                ExecutionTraceEventFieldValue.FromString(phase);

            if (durationMs.HasValue)
            {
                evt.Fields[ExecutionTraceEventFields.DurationMs] =
                    ExecutionTraceEventFieldValue.FromLong(durationMs.Value);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                evt.Fields[ExecutionTraceEventFields.Error] =
                    ExecutionTraceEventFieldValue.FromString(error);
            }

            return evt;
        }
    }
}


