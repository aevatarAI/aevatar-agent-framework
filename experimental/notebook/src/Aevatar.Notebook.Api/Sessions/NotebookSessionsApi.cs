using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Notebook.Agents;
using Aevatar.Notebook.Api.AgUi;
using Aevatar.Notebook.Api.Contracts;
using Aevatar.Notebook.Api.Infrastructure;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Streaming;
using Aevatar.Notebook.Tracing;
using Microsoft.Extensions.Options;

namespace Aevatar.Notebook.Api.Sessions;

// ============================================================
//  Notebook Sessions API (AG-UI)
//
//  Endpoints:
//  - POST /api/sessions
//  - GET  /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events
// ============================================================

internal static class NotebookSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapNotebookSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapList(app);
        MapInput(app);
        MapAgUiEvents(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", (CreateSessionInDto? input, NotebookSessionManager sessions) =>
        {
            var s = sessions.Create(input?.ProviderName);
            return Results.Json(new { ok = true, sessionId = s.Id });
        });
    }

    private static void MapList(WebApplication app)
    {
        app.MapGet("/api/sessions", (NotebookSessionManager sessions) =>
        {
            var list = sessions.ListSessions();
            return Results.Json(new { count = list.Count, sessions = list });
        });
    }

    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            NotebookSessionManager sessions,
            NotebookRuntime runtime,
            NotebookContextBuilder contextBuilder,
            IExecutionTraceStore traceStore,
            IOptions<LLMProvidersConfig> llm,
            ILogger<NotebookRuntime> logger,
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
                    contextBuilder,
                    traceStore,
                    llm,
                    logger,
                    CancellationToken.None);
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            NotebookSessionManager sessions,
            NotebookRuntime runtime,
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
                var line = JsonSerializer.Serialize(evt, Json);
                await writer.WriteAsync("data: ");
                await writer.WriteLineAsync(line);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
            }

            long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

            // 1) Snapshot-first (recommended by AG-UI guide)
            var bootstrapMessages = await NotebookAgUiBootstrap.BuildMessagesSnapshotAsync(
                session.Id,
                runtime,
                maxMessages: 60,
                ct);

            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = bootstrapMessages
            }, ct);

            await WriteSseAsync(new CustomEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Name = "aevatar.notebook.session",
                Value = new
                {
                    sessionId = session.Id,
                    createdAt = session.CreatedAt.ToString("O"),
                    providerName = session.ProviderName ?? ""
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
        NotebookSession session,
        string runId,
        SessionInputInDto input,
        NotebookRuntime runtime,
        NotebookContextBuilder contextBuilder,
        IExecutionTraceStore traceStore,
        IOptions<LLMProvidersConfig> llm,
        ILogger<NotebookRuntime> logger,
        CancellationToken ct)
    {
        var startedAtUtc = DateTime.UtcNow;
        var executionId = $"notebook-session-chat-{Guid.NewGuid():N}";

        string? agentId = null;
        NotebookContextBuildResult? ctxResult = null;
        ChatResponse? resp = null;
        string? error = null;

        // Streamed content buffer (for trace + run finished result).
        var assistant = new StringBuilder(capacity: 1024);

        // Serialize runs per session.
        await session.RunLock.WaitAsync(ct);
        try
        {
            var providerOverride = session.ProviderName;
            var (agent, aid) = await runtime.GetAgentAsync(session.Id, providerOverride, ct);
            agentId = aid;

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

            // Build notebook context (sources -> slices -> rendered prompt injection)
            ctxResult = await contextBuilder.BuildAsync(
                new NotebookContextBuildRequest
                {
                    Query = input.Message.Trim(),
                    SelectedSourceIds = input.SelectedSourceIds
                },
                ct: ct);

            var requestId = input.RequestId ?? Guid.NewGuid().ToString("N");
            var request = new ChatRequest
            {
                Message = input.Message.Trim(),
                RequestId = requestId,
                StageHint = "session:chat"
            };

            request.Context["execution_id"] = executionId;
            request.Context[NotebookAgent.NotebookContextKey] = ctxResult.Rendered;

            var citations = ctxResult.Context.Slices
                .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
                .Select(s => new
                {
                    sourceId = s.SourceId,
                    chunkId = s.ChunkId,
                    score = s.Score,
                    reason = s.Reason,
                    preview = string.IsNullOrWhiteSpace(s.Content) ? "" : (s.Content.Length <= 180 ? s.Content : s.Content[..180])
                })
                .ToList();

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.notebook.chat_meta",
                Value = new
                {
                    agentId = agentId ?? "",
                    requestId,
                    executionId,
                    context = new
                    {
                        sourceIds = ctxResult.SourceIds,
                        strategy = ctxResult.Context.Tags.TryGetValue("strategy", out var st) ? st : "",
                        chunkSlices = citations.Count,
                        renderedChars = (ctxResult.Rendered ?? string.Empty).Length
                    },
                    citations
                }
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

            // Bind tool progress events to AG-UI stream (best-effort).
            var prevSink = NotebookStreamEventContext.Current;
            NotebookStreamEventContext.Current = new AgUiNotebookStreamEventSink(session.Events, threadId: session.Id, runId);
            try
            {
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
                NotebookStreamEventContext.Current = prevSink;
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

            // Synthesized response for tracing / run result.
            resp ??= new ChatResponse { Content = assistant.ToString(), RequestId = input.RequestId ?? "" };

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = true }
            });
        }
        catch (Exception ex)
        {
            error = ex is OperationCanceledException
                ? "run canceled"
                : ex.Message;

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = error,
                Code = "NOTEBOOK_RUN_ERROR"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error }
            });

            logger.LogError(ex, "[Notebook] Session run failed: {Message}", ex.Message);
        }
        finally
        {
            session.RunLock.Release();

            // Best-effort trace save (auto graph projection)
            try
            {
                var endedAtUtc = DateTime.UtcNow;
                var trace = NotebookTraceBuilder.BuildChatTrace(new NotebookChatTraceData
                {
                    ExecutionId = executionId,
                    AgentId = agentId ?? string.Empty,
                    RequestId = input.RequestId ?? string.Empty,
                    Query = input.Message.Trim(),
                    SourceIds = ctxResult?.SourceIds ?? new List<string>(),
                    Context = ctxResult?.Context,
                    RenderedContext = ctxResult?.Rendered,
                    Response = resp ?? new ChatResponse { Content = assistant.ToString(), RequestId = input.RequestId ?? "" },
                    Error = error,
                    LlmProvider = llm.Value.Default,
                    StartedAtUtc = startedAtUtc,
                    EndedAtUtc = endedAtUtc
                });

                await traceStore.SaveAsync(trace, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "[Notebook] Session trace save failed (best-effort).");
            }
        }
    }

    // ============================================================
    //  Tool progress → AG-UI projection (CUSTOM events)
    // ============================================================
    private sealed class AgUiNotebookStreamEventSink : INotebookStreamEventSink
    {
        private readonly BroadcastEventHub<AgUiEvent> _hub;
        private readonly string _threadId;
        private readonly string _runId;

        public AgUiNotebookStreamEventSink(BroadcastEventHub<AgUiEvent> hub, string threadId, string runId)
        {
            _hub = hub;
            _threadId = threadId;
            _runId = runId;
        }

        public Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct)
        {
            var messageId = $"msg:{_threadId}:tool:{toolCallId}";

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
                Name = "aevatar.notebook.tool_start",
                Value = new { threadId = _threadId, runId = _runId, toolCallId, toolName }
            });
            return Task.CompletedTask;
        }

        public Task EmitToolEndAsync(
            string toolCallId,
            string toolName,
            bool success,
            long durationMs,
            string? error,
            CancellationToken ct)
        {
            var messageId = $"msg:{_threadId}:tool:{toolCallId}";

            _hub.Publish(new ToolCallResultEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                ToolCallId = toolCallId,
                Result = error ?? (success ? "Success" : "Failed")
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
                Name = "aevatar.notebook.tool_end",
                Value = new { threadId = _threadId, runId = _runId, toolCallId, toolName, success, durationMs, error = Trunc(error, 240) }
            });
            return Task.CompletedTask;
        }

        private static string Trunc(string? s, int maxChars)
        {
            var x = (s ?? string.Empty).Replace("\r", "").Trim();
            if (x.Length <= maxChars) return x;
            return x[..maxChars];
        }
    }
}


