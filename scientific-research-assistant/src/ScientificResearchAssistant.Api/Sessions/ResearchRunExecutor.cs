using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Streaming;

namespace ScientificResearchAssistant.Api.Sessions;

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
    private readonly IOptions<LLMProvidersConfig> _llm;
    private readonly ILogger<ResearchRunExecutor> _logger;

    public ResearchRunExecutor(
        ResearchRuntime runtime,
        MaterialsService materials,
        WorkspaceService workspace,
        IOptions<LLMProvidersConfig> llm,
        ILogger<ResearchRunExecutor> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
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

        var mode = (input.Mode ?? "chat").Trim().ToLowerInvariant();
        if (mode is "vibe" or "vibe_researching" or "axiom")
        {
            await ExecuteVibeResearchingRunAsync(session, runId, input, ct);
            return;
        }

        await ExecuteChatRunAsync(session, runId, input, ct);
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

        // Serialize runs per session.
        await session.RunLock.WaitAsync(ct);
        try
        {
            var providerOverride = session.ProviderName;

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
            var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });

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
        catch (Exception ex)
        {
            error = ex is OperationCanceledException ? "run canceled" : ex.Message;

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
            session.RunLock.Release();
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

        await session.RunLock.WaitAsync(ct);
        try
        {
            var providerOverride = session.ProviderName;
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
            var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });

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

                var snapshot = await _materials.LoadAsync(session.Id, query: question, ct);
                HydrateWorkspace(session, runId, question, snapshot);
                ApplyKnowledgeToWorkspace(session, _workspace.ScanWorkspace(session.Id));

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
                // Step 2) Planner agent
                // ------------------------------------------------------------
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.plan"
                });

                EmitAssistantDelta(session, assistantMessageId, "### Plan (multi-agent)\n");
                assistant.Append("### Plan (multi-agent)\n");

                var (planner, plannerId) = await _runtime.GetPlannerAgentAsync(session.Id, providerOverride, ct);
                var planRequest = new ChatRequest
                {
                    Message = question,
                    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                    StageHint = "session:vibe:plan"
                };
                planRequest.Context["agent_id"] = plannerId;
                planRequest.Context["materials_context"] = snapshot.RenderedContext;

                await foreach (var chunk in planner.ChatStreamAsync(planRequest, ct))
                {
                    if (string.IsNullOrEmpty(chunk)) continue;
                    assistant.Append(chunk);
                    EmitAssistantDelta(session, assistantMessageId, chunk);
                }

                EmitAssistantDelta(session, assistantMessageId, "\n\n");
                assistant.Append("\n\n");

                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.plan"
                });

                // ------------------------------------------------------------
                // Step 3) Reasoner agent (axiom-grounded)
                // ------------------------------------------------------------
                session.Events.Publish(new StepStartedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.reason"
                });

                EmitAssistantDelta(session, assistantMessageId, "### Reasoning (axioms + references)\n");
                assistant.Append("### Reasoning (axioms + references)\n");

                var (reasoner, reasonerId) = await _runtime.GetReasonerAgentAsync(session.Id, providerOverride, ct);
                var reasonRequest = new ChatRequest
                {
                    Message = question,
                    RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                    StageHint = "session:vibe:reason"
                };
                reasonRequest.Context["agent_id"] = reasonerId;
                reasonRequest.Context["materials_context"] = snapshot.RenderedContext;

                // Best-effort: make sure tools snapshot includes python tool if enabled.
                _ = await _runtime.RefreshToolsSnapshotAsync(session.Id, providerOverride, ct);

                var supportsStreaming = await reasoner.SupportsStreamingAsync(ct);
                if (!supportsStreaming)
                {
                    var resp = await reasoner.ChatAsync(reasonRequest, ct);
                    var text = resp.Content ?? string.Empty;
                    if (text.Length > 0)
                    {
                        assistant.Append(text);
                        EmitAssistantDelta(session, assistantMessageId, text);
                    }
                }
                else
                {
                    await foreach (var chunk in reasoner.ChatStreamAsync(reasonRequest, ct))
                    {
                        if (string.IsNullOrEmpty(chunk)) continue;
                        assistant.Append(chunk);
                        EmitAssistantDelta(session, assistantMessageId, chunk);
                    }
                }

                session.Events.Publish(new StepFinishedEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    StepName = "vibe.reason"
                });

                // ------------------------------------------------------------
                // Done
                // ------------------------------------------------------------
                session.Events.Publish(new TextMessageEndEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    MessageId = assistantMessageId
                });

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
        catch (Exception ex)
        {
            error = ex is OperationCanceledException ? "run canceled" : ex.Message;

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
            session.RunLock.Release();
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

        ws.Materials.RootDir = "";
        ws.Materials.LoadedAt = snapshot.LoadedAt.ToString("O");

        ws.Materials.Items = new List<MaterialMeta>(capacity: snapshot.Facts.Count + snapshot.Sources.Count);
        foreach (var x in snapshot.Facts)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }
        foreach (var x in snapshot.Sources)
        {
            ws.Materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        ws.Materials.ContextPreview = Trunc(snapshot.RenderedContext, 2000);

        ws.Vibe.LastRunId = runId;
        ws.Vibe.LastGoal = question;
        ws.Vibe.Steps = ["vibe.materials", "vibe.plan", "vibe.reason"];
    }

    private static void ApplyKnowledgeToWorkspace(ResearchSession session, WorkspaceScanResult scan)
    {
        var k = session.Workspace.Knowledge;
        k.FactsCount = scan.FactsCount;
        k.FactsProposedCount = scan.FactsProposedCount;
        k.SourcesCount = scan.SourcesCount;
        k.FactsProposedRecent = scan.FactsProposedRecent;
    }

    private static string Trunc(string? s, int maxChars)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= maxChars) return t;
        return t[..maxChars];
    }

    // ============================================================
    //  Tool progress → AG-UI projection (CUSTOM events)
    // ============================================================
    private sealed class AgUiResearchStreamEventSink : IResearchStreamEventSink
    {
        private readonly ResearchRuntime _runtime;
        private readonly string _sessionId;
        private readonly ScientificResearchAssistant.Api.Infrastructure.BroadcastEventHub<AgUiEvent> _hub;
        private readonly string _threadId;
        private readonly string _runId;

        public AgUiResearchStreamEventSink(
            ResearchRuntime runtime,
            string sessionId,
            ScientificResearchAssistant.Api.Infrastructure.BroadcastEventHub<AgUiEvent> hub,
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

            _hub.Publish(new ToolCallStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                ToolCallId = toolCallId,
                ToolName = toolName
            });

            _hub.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.scientific.tool_start",
                Value = new { threadId = _threadId, runId = _runId, toolCallId, toolName, isMcp }
            });
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

            _hub.Publish(new ToolCallResultEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                ToolCallId = toolCallId,
                Result = resultPreview ?? (error ?? string.Empty)
            });

            _hub.Publish(new ToolCallEndEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                ToolCallId = toolCallId
            });

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
    }
}


