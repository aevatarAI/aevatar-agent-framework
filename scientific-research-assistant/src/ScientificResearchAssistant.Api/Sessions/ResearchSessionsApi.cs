using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.AgUi;
using ScientificResearchAssistant.Api.Infrastructure;
using ScientificResearchAssistant.Streaming;

namespace ScientificResearchAssistant.Api.Sessions;

// ============================================================
//  Scientific Research Assistant Sessions API (AG-UI)
//
//  Endpoints:
//  - GET  /health
//  - GET  /api/info
//  - POST /api/sessions
//  - GET  /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events   (SSE, snapshot-first)
// ============================================================

internal static class ResearchSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapResearchSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapTools(app);
        MapInput(app);
        MapMcpReconnect(app);
        MapAgUiEvents(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", (CreateSessionInDto? input, ResearchSessionManager sessions) =>
        {
            var s = sessions.Create(input?.ProviderName);
            return Results.Json(new { ok = true, sessionId = s.Id });
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/sessions", (ResearchSessionManager sessions) =>
        {
            var list = sessions.ListSessions();
            return Results.Json(new { count = list.Count, sessions = list });
        });
    }

    private static void MapTools(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/tools", async (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            try
            {
                var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                return Results.Json(new { ok = true, sessionId = session.Id, tools });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "tools snapshot failed", detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            IOptions<LLMProvidersConfig> llm,
            ILogger<ResearchRuntime> logger,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                await ExecuteChatRunAsync(
                    session,
                    runId,
                    input,
                    runtime,
                    llm,
                    logger,
                    CancellationToken.None);
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapMcpReconnect(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/mcp/reconnect", (
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            // Fire-and-forget; result is streamed back via SSE (CUSTOM events + tools_snapshot refresh).
            _ = Task.Run(async () =>
            {
                long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.mcp_reconnect_started",
                    Value = new { sessionId = session.Id }
                });

                try
                {
                    var (agent, _) = await runtime.GetAgentAsync(session.Id, session.ProviderName, CancellationToken.None);
                    var (ok, error) = await agent.ReconnectMcpAsync(CancellationToken.None);

                    // Always refresh the tools snapshot so UI updates immediately (even if reconnect failed).
                    var (tools, _mcpNames) = await runtime.RefreshToolsSnapshotAsync(session.Id, session.ProviderName, CancellationToken.None);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });

                    if (ok)
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_finished",
                            Value = new { sessionId = session.Id, ok = true }
                        });
                    }
                    else
                    {
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = Ts(),
                            Name = "aevatar.scientific.mcp_reconnect_error",
                            Value = new { sessionId = session.Id, ok = false, error = error ?? "mcp reconnect failed" }
                        });
                    }
                }
                catch (Exception ex)
                {
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(),
                        Name = "aevatar.scientific.mcp_reconnect_error",
                        Value = new { sessionId = session.Id, ok = false, error = ex.Message }
                    });
                }
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true });
        });
    }

    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            ResearchSessionManager sessions,
            ResearchRuntime runtime,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { error = "session not found" }, cancellationToken: ct);
                return;
            }

            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.Pragma = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.StartAsync(ct);

            await using var writer = new StreamWriter(
                http.Response.Body,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
                leaveOpen: true);

            async Task WriteSseAsync(AgUiEvent evt, CancellationToken token)
            {
                // IMPORTANT:
                // System.Text.Json does not polymorphically serialize derived members when using the generic overload.
                // Here `evt` is typed as AgUiEvent, so Serialize(evt, options) would only output base fields
                // (type/timestamp/rawEvent) and drop derived fields like messageId/delta/name/value.
                // Use runtime type to keep AG-UI protocol payloads intact.
                var line = JsonSerializer.Serialize((object)evt, evt.GetType(), Json);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

            // ------------------------------------------------------------
            //  0) Fast bootstrap (NEVER block SSE on agent/tool init)
            //
            //  WHY:
            //  - Agent initialization may involve MCP/tool bootstrap and can be slow or flaky.
            //  - If we await it here, frontend sees an "empty" chat for a long time.
            //  - So we send a minimal snapshot immediately, then hydrate in background.
            // ------------------------------------------------------------
            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = []
            }, ct);

            await WriteSseAsync(new CustomEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Name = "aevatar.scientific.session",
                Value = new
                {
                    sessionId = session.Id,
                    createdAt = session.CreatedAt.ToString("O"),
                    providerName = session.ProviderName ?? ""
                }
            }, ct);

            // 1) Background hydration (snapshot + tools catalog) -> publish into the same hub.
            _ = Task.Run(async () =>
            {
                try
                {
                    var bootstrapMessages = await ResearchAgUiBootstrap.BuildMessagesSnapshotAsync(
                        session.Id,
                        runtime,
                        maxMessages: 60,
                        ct);

                    session.Events.Publish(new MessagesSnapshotEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Messages = bootstrapMessages
                    });
                }
                catch
                {
                    // best-effort
                }
            }, ct);

            _ = Task.Run(async () =>
            {
                try
                {
                    var (tools, _mcpNames) = await runtime.GetToolsSnapshotAsync(session.Id, session.ProviderName, ct);
                    session.Events.Publish(new CustomEvent
                    {
                        Timestamp = Ts(DateTimeOffset.UtcNow),
                        Name = "aevatar.scientific.tools_snapshot",
                        Value = new { sessionId = session.Id, tools }
                    });
                }
                catch
                {
                    // best-effort
                }
            }, ct);

            // 2) Live stream (no replay; snapshot already hydrated UI)
            await foreach (var evt in session.Events.SubscribeAsync(replay: false, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                await WriteSseAsync(evt, ct);
            }
        });
    }

    private static async Task ExecuteChatRunAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        ResearchRuntime runtime,
        IOptions<LLMProvidersConfig> llm,
        ILogger<ResearchRuntime> logger,
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
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = userMessageId,
                Role = "user"
            });
            session.Events.Publish(new TextMessageContentEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = userMessageId,
                Delta = input.Message.Trim()
            });
            session.Events.Publish(new TextMessageEndEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = userMessageId
            });

            // Emit assistant message stream
            var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });

            // Initialize agent AFTER we've notified UI that the run started (avoids "blank" UI when init is slow).
            var (agent, agentId) = await runtime.GetAgentAsync(session.Id, providerOverride, ct);

            // Best-effort: pre-load tools so MCP tool names are known (for UI tags).
            _ = await runtime.GetToolsSnapshotAsync(session.Id, providerOverride, ct);

            // Bind tool progress events to AG-UI stream (best-effort).
            var prevSink = ResearchStreamEventContext.Current;
            ResearchStreamEventContext.Current = new AgUiResearchStreamEventSink(
                runtime,
                sessionId: session.Id,
                hub: session.Events,
                threadId: session.Id,
                runId: runId);

            try
            {
                var requestId = input.RequestId ?? Guid.NewGuid().ToString("N");
                var request = new ChatRequest
                {
                    Message = input.Message.Trim(),
                    RequestId = requestId,
                    StageHint = "session:chat"
                };

                // Helpful metadata for debugging / tracing
                request.Context["agent_id"] = agentId;
                request.Context["llm_default"] = llm.Value.Default ?? "";

                var supportsStreaming = await agent.SupportsStreamingAsync(ct);
                if (!supportsStreaming)
                {
                    resp = await agent.ChatAsync(request, ct);
                    var text = resp.Content ?? string.Empty;
                    if (text.Length > 0)
                    {
                        assistant.Append(text);
                        session.Events.Publish(new TextMessageContentEvent
                        {
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            MessageId = assistantMessageId,
                            Delta = text
                        });
                    }
                }
                else
                {
                    await foreach (var chunk in agent.ChatStreamAsync(request, ct))
                    {
                        if (string.IsNullOrEmpty(chunk))
                            continue;

                        assistant.Append(chunk);
                        session.Events.Publish(new TextMessageContentEvent
                        {
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            MessageId = assistantMessageId,
                            Delta = chunk
                        });
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

            logger.LogError(ex, "[Scientific] Session run failed: {Message}", ex.Message);
        }
        finally
        {
            session.RunLock.Release();
        }
    }

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

    internal sealed record CreateSessionInDto(string? ProviderName);

    internal sealed record SessionInputInDto(string Message, string? RequestId);
}


