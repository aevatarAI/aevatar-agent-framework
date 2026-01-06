using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI.Abstractions.Configuration;
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
            IOptions<LLMProvidersConfig> llm,
            ILogger<LearningSessionManager> logger,
            CancellationToken ct) =>
        {
            if (!sessions.TryGet(sessionId, out var session))
                return Results.NotFound(new { error = "session not found" });

            if (string.IsNullOrWhiteSpace(input.Message))
                return Results.BadRequest(new { error = "message is required" });

            if (!string.IsNullOrWhiteSpace(input.ProviderName))
                session.ProviderName = input.ProviderName.Trim();

            // Fire-and-forget run; clients receive progress via AG-UI SSE.
            var runSeq = session.NextRunSeq();
            var runId = $"{session.Id}:{runSeq}";

            _ = Task.Run(async () =>
            {
                await ExecuteSkeletonRunAsync(session, runId, input, llm, logger, CancellationToken.None);
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

    private static async Task ExecuteSkeletonRunAsync(
        LearningSession session,
        string runId,
        SessionInputInDto input,
        IOptions<LLMProvidersConfig> llm,
        ILogger logger,
        CancellationToken ct)
    {
        // Serialize runs per session.
        await session.RunLock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
            session.AppendMessage(new AgUiMessage { Id = userMessageId, Role = "user", Content = userText });

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

            // Emit assistant placeholder (real AI wiring comes later).
            var defaultProvider = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
            var providerHint = session.ProviderName ?? llm.Value.Default ?? "";

            var assistantText =
                """
                (MVP skeleton)
                已收到输入。后续任务会把 LLMProviders + Notebook context + streaming 真正接入这里。
                """.Trim();

            var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
            session.AppendMessage(new AgUiMessage { Id = assistantMessageId, Role = "assistant", Content = assistantText });

            session.Events.Publish(new CustomEvent
            {
                Timestamp = now,
                Name = "aevatar.learning.run_meta",
                Value = new
                {
                    threadId = session.Id,
                    runId,
                    llmDefault = defaultProvider,
                    providerName = providerHint
                }
            });

            session.Events.Publish(new TextMessageStartEvent
            {
                Timestamp = now,
                MessageId = assistantMessageId,
                Role = "assistant"
            });
            session.Events.Publish(new TextMessageContentEvent
            {
                Timestamp = now,
                MessageId = assistantMessageId,
                Delta = assistantText
            });
            session.Events.Publish(new TextMessageEndEvent
            {
                Timestamp = now,
                MessageId = assistantMessageId
            });

            session.Events.Publish(new StepFinishedEvent
            {
                Timestamp = now,
                StepName = "chat"
            });

            session.Events.Publish(new RunFinishedEvent
            {
                Timestamp = now,
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
    private sealed record SessionInputInDto(string Message, string? ProviderName);

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
        public SemaphoreSlim RunLock { get; } = new(1, 1);
        public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0);

        public int NextRunSeq() => Interlocked.Increment(ref _runSeq);

        public void AppendMessage(AgUiMessage msg)
        {
            if (msg == null) return;

            lock (_messagesLock)
            {
                _messages.Add(msg);

                // Keep bounded in-memory transcript (MVP).
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

    // ============================================================
    //  BroadcastEventHub<T> (copied pattern from notebook)
    //  Purpose: allow multiple SSE subscribers without stealing events.
    // ============================================================

    internal sealed class BroadcastEventHub<T>
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, Channel<T>> _subscribers = new();
        private ChannelWriter<T>[] _writersSnapshot = [];
        private readonly Queue<T> _replay;
        private readonly int _replayBufferSize;
        private int _nextSubscriberId;
        private bool _completed;

        public BroadcastEventHub(int replayBufferSize = 0)
        {
            _replayBufferSize = Math.Max(0, replayBufferSize);
            _replay = new Queue<T>(_replayBufferSize > 0 ? _replayBufferSize : 4);
        }

        public void Publish(T evt)
        {
            // Boundary layer: never throw.
            try
            {
                ChannelWriter<T>[] writers;
                lock (_lock)
                {
                    if (_completed) return;

                    if (_replayBufferSize > 0)
                    {
                        _replay.Enqueue(evt);
                        while (_replay.Count > _replayBufferSize)
                            _replay.Dequeue();
                    }

                    writers = _writersSnapshot;
                }

                foreach (var w in writers)
                    w.TryWrite(evt);
            }
            catch
            {
                // ignored
            }
        }

        public void Complete()
        {
            ChannelWriter<T>[] writers;
            lock (_lock)
            {
                if (_completed) return;
                _completed = true;
                writers = _writersSnapshot;
                _subscribers.Clear();
                _writersSnapshot = [];
            }

            foreach (var w in writers)
                w.TryComplete();
        }

        public async IAsyncEnumerable<T> SubscribeAsync(
            bool replay = false,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            List<T>? snapshot = null;
            Channel<T>? ch = null;
            var id = 0;

            lock (_lock)
            {
                if (replay && _replay.Count > 0)
                    snapshot = _replay.ToList();

                if (_completed)
                {
                    ch = null;
                }
                else
                {
                    id = ++_nextSubscriberId;
                    ch = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = true
                    });
                    _subscribers[id] = ch;
                    RefreshWritersSnapshotLocked();
                }
            }

            if (snapshot is not null)
            {
                foreach (var item in snapshot)
                    yield return item;
            }

            if (ch is null)
                yield break;

            try
            {
                await foreach (var item in ch.Reader.ReadAllAsync(ct))
                    yield return item;
            }
            finally
            {
                lock (_lock)
                {
                    _subscribers.Remove(id);
                    RefreshWritersSnapshotLocked();
                }

                ch.Writer.TryComplete();
            }
        }

        private void RefreshWritersSnapshotLocked()
        {
            _writersSnapshot = _subscribers.Count == 0
                ? []
                : _subscribers.Values.Select(x => x.Writer).ToArray();
        }
    }
}


