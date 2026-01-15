using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Learning.Chat;
using Aevatar.Learning.Notebooks;
using Microsoft.Extensions.Options;

namespace Aevatar.Learning.Api.Sessions;

// ============================================================
//  Learning Sessions API (AG-UI)
//
//  Endpoints:
//  - POST /api/sessions
//  - POST /api/sessions/{id}/input
//  - GET  /api/sessions/{id}/agui/events
//
//  Notes:
//  - Snapshot-first reconnect (AG-UI best practice)
//  - replay is intentionally disabled (replay:false)
// ============================================================
internal static class LearningSessionsApi
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapLearningSessionsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreate(app);
        MapInput(app);
        MapAgUiEvents(app);
    }

    private static void MapCreate(WebApplication app)
    {
        app.MapPost("/api/sessions", (CreateSessionInDto? input, LearningSessionManager sessions) =>
        {
            var s = sessions.Create(input?.ProviderName);
            return Results.Json(new { ok = true, sessionId = s.Id });
        });
    }

    private static void MapInput(WebApplication app)
    {
        app.MapPost("/api/sessions/{sessionId}/input", async (
            string sessionId,
            SessionInputInDto input,
            LearningSessionManager sessions,
            NotebookDirectoryStore notebooks,
            LearningChatService chat,
            ILogger<LearningSessionManager> logger,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            if (!string.IsNullOrWhiteSpace(input.ProviderName))
                session.ProviderName = input.ProviderName.Trim();

            if (!string.IsNullOrWhiteSpace(input.NotebookId))
                session.NotebookId = input.NotebookId.Trim();

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                await ExecuteChatRunAsync(session, runId, input, notebooks, chat, logger, CancellationToken.None);
            }, CancellationToken.None);

            return Results.Accepted($"/api/sessions/{session.Id}", new { ok = true, sessionId = session.Id, runId });
        });
    }

    private static void MapAgUiEvents(WebApplication app)
    {
        app.MapGet("/api/sessions/{sessionId}/agui/events", async (
            HttpContext http,
            string sessionId,
            LearningSessionManager sessions,
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
            var messages = session.SnapshotMessages(maxMessages: 60);
            await WriteSseAsync(new MessagesSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Messages = messages
            }, ct);

            await WriteSseAsync(new CustomEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Name = "aevatar.learning.session",
                Value = new
                {
                    sessionId = session.Id,
                    createdAt = session.CreatedAt.ToString("O"),
                    providerName = session.ProviderName ?? "",
                    notebookId = session.NotebookId ?? ""
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
        LearningSession session,
        string runId,
        SessionInputInDto input,
        NotebookDirectoryStore notebooks,
        LearningChatService chat,
        ILogger logger,
        CancellationToken ct)
    {
        // Serialize runs per session.
        await session.RunLock.WaitAsync(ct);
        try
        {
            long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var now = Ts();

            // RUN_STARTED first
            session.Events.Publish(new RunStartedEvent
            {
                Timestamp = now,
                ThreadId = session.Id,
                RunId = runId
            });

            session.Events.Publish(new StepStartedEvent
            {
                Timestamp = now,
                StepName = "chat"
            });

            // Emit user message
            var userText = (input.Message ?? string.Empty).Trim();
            var userMessageId = $"msg:{session.Id}:user:{runId}";
            session.UpsertMessage(userMessageId, "user", userText);

            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = userMessageId,
                Role = "user"
            });
            session.Events.Publish(new TextMessageContentEvent
            {
                Timestamp = now,
                MessageId = userMessageId,
                Delta = userText
            });
            session.Events.Publish(new TextMessageEndEvent
            {
                Timestamp = now,
                MessageId = userMessageId
            });

            // Resolve notebook workspace (MVP: require notebookId or auto-pick when only 1 notebook exists)
            var notebookId = (input.NotebookId ?? session.NotebookId ?? string.Empty).Trim();
            NotebookInfo? notebook = null;
            if (notebookId.Length > 0)
                notebook = notebooks.GetNotebook(notebookId);

            if (notebook == null)
            {
                var list = notebooks.ListNotebooks();
                if (list.Count == 1)
                    notebook = list[0];
            }

            if (notebook == null)
                throw new InvalidOperationException("notebookId is required (create/select a notebook first).");

            session.NotebookId = notebook.NotebookId;
            var rootDir = Path.GetDirectoryName(notebook.DirectoryPath) ?? string.Empty;
            var workspace = new NotebookWorkspace(notebook.NotebookId, rootDir, notebook.DirectoryPath);

            // Start real chat run (context + provider selection + streaming tokens)
            var run = await chat.StartAsync(new LearningChatInput(
                Workspace: workspace,
                Message: userText,
                ProviderName: session.ProviderName,
                SelectedSourceIds: null,
                Budget: null,
                History: null), ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = Ts(),
                Name = "aevatar.learning.context",
                Value = new
                {
                    notebookId = notebook.NotebookId,
                    providerName = run.ProviderName,
                    budget = new
                    {
                        maxTotalChars = run.Context.Budget.MaxTotalChars,
                        maxPerSourceChars = run.Context.Budget.MaxPerSourceChars,
                        maxSources = run.Context.Budget.MaxSources
                    },
                    sources = run.Context.Slices.Select(s => new
                    {
                        sourceId = s.SourceId,
                        title = s.Title,
                        mimeType = s.MimeType,
                        reason = s.Reason,
                        originalChars = s.OriginalChars,
                        preview = string.IsNullOrEmpty(s.Content)
                            ? ""
                            : (s.Content.Length <= 160 ? s.Content : s.Content[..160] + "...")
                    }).ToList()
                }
            });

            var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
            session.UpsertMessage(assistantMessageId, "assistant", string.Empty);

            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = Ts(),
                MessageId = assistantMessageId,
                Role = "assistant"
            });

            await foreach (var token in run.Tokens.WithCancellation(ct))
            {
                var delta = (token?.Content ?? string.Empty);
                if (delta.Length == 0) continue;

                session.AppendMessageDelta(assistantMessageId, "assistant", delta);
                session.Events.Publish(new TextMessageContentEvent
                {
                    Timestamp = Ts(),
                    MessageId = assistantMessageId,
                    Delta = delta
                });
            }

            session.Events.Publish(new TextMessageEndEvent
            {
                Timestamp = Ts(),
                MessageId = assistantMessageId
            });

            session.Events.Publish(new StepFinishedEvent
            {
                Timestamp = Ts(),
                StepName = "chat"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = Ts(),
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = true }
            });
        }
        catch (Exception ex)
        {
            var error = ex is OperationCanceledException ? "run canceled" : ex.Message;
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            session.Events.Publish(new RunErrorEvent
            {
                Timestamp = ts,
                Message = error,
                Code = "LEARNING_RUN_ERROR"
            });
            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = ts,
                ThreadId = session.Id,
                RunId = runId,
                Result = new { ok = false, error }
            });

            logger.LogError(ex, "[Learning] Session run failed: {Message}", ex.Message);
        }
        finally
        {
            session.RunLock.Release();
        }
    }

    // ============================================================
    //  Contracts (HTTP DTO)
    // ============================================================

    private sealed record CreateSessionInDto(string? ProviderName);
    private sealed record SessionInputInDto(string Message, string? ProviderName, string? NotebookId);

    // ============================================================
    //  In-memory session manager (MVP)
    // ============================================================

    internal sealed class LearningSessionManager
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, LearningSession> _sessions = new(StringComparer.Ordinal);

        public LearningSession Create(string? providerName)
        {
            var id = Guid.NewGuid().ToString("N");
            var s = new LearningSession(id)
            {
                ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim()
            };

            lock (_lock)
            {
                _sessions[id] = s;
            }

            return s;
        }

        public bool TryGet(string sessionId, out LearningSession session)
        {
            sessionId = (sessionId ?? string.Empty).Trim();
            if (sessionId.Length == 0)
            {
                session = null!;
                return false;
            }

            lock (_lock)
            {
                return _sessions.TryGetValue(sessionId, out session!);
            }
        }
    }

    internal sealed class LearningSession
    {
        private const string EventsHubName = "LearningSession.Events";
        private readonly object _messagesLock = new();
        private readonly List<AgUiMessage> _messages = new(capacity: 64);
        private int _runSeq;

        public LearningSession(string id)
        {
            Id = id;
        }

        public string Id { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public string? ProviderName { get; set; }
        public string? NotebookId { get; set; }
        public SemaphoreSlim RunLock { get; } = new(1, 1);
        public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0, hubName: EventsHubName);

        public int NextRunSeq() => Interlocked.Increment(ref _runSeq);

        public void UpsertMessage(string id, string role, string content)
        {
            id = (id ?? string.Empty).Trim();
            role = (role ?? "assistant").Trim();
            content ??= string.Empty;

            if (id.Length == 0) return;

            lock (_messagesLock)
            {
                var idx = _messages.FindIndex(x => string.Equals(x.Id, id, StringComparison.Ordinal));
                if (idx >= 0)
                {
                    _messages[idx] = new AgUiMessage { Id = id, Role = role, Content = content };
                }
                else
                {
                    _messages.Add(new AgUiMessage { Id = id, Role = role, Content = content });
                }

                // Keep bounded in-memory transcript (MVP).
                if (_messages.Count > 400)
                    _messages.RemoveRange(0, Math.Max(0, _messages.Count - 400));
            }
        }

        public void AppendMessageDelta(string id, string role, string delta)
        {
            id = (id ?? string.Empty).Trim();
            role = (role ?? "assistant").Trim();
            delta ??= string.Empty;

            if (id.Length == 0 || delta.Length == 0) return;

            lock (_messagesLock)
            {
                var idx = _messages.FindIndex(x => string.Equals(x.Id, id, StringComparison.Ordinal));
                if (idx >= 0)
                {
                    var cur = _messages[idx];
                    _messages[idx] = new AgUiMessage
                    {
                        Id = cur.Id,
                        Role = cur.Role,
                        Content = (cur.Content ?? string.Empty) + delta
                    };
                }
                else
                {
                    _messages.Add(new AgUiMessage { Id = id, Role = role, Content = delta });
                }

                if (_messages.Count > 400)
                    _messages.RemoveRange(0, Math.Max(0, _messages.Count - 400));
            }
        }

        public List<AgUiMessage> SnapshotMessages(int maxMessages)
        {
            maxMessages = Math.Clamp(maxMessages, 0, 200);
            if (maxMessages == 0) return [];

            lock (_messagesLock)
            {
                if (_messages.Count == 0) return [];

                var take = Math.Min(maxMessages, _messages.Count);
                var slice = _messages.Skip(Math.Max(0, _messages.Count - take)).ToList();
                return slice;
            }
        }
    }

}


