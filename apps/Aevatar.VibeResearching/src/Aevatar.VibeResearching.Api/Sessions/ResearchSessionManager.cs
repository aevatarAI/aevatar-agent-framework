using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Sessions.Runtime;
using Aevatar.Agents.Core.Runtime;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using VibeResearching.Api.Vibe;
using VibeResearching.Contracts.Sessions;

namespace VibeResearching.Api.Sessions;

// ============================================================
//  ResearchSessionManager (MVP)
//
//  - In-memory session registry + optional DB-backed index.
//  - Each session owns an AG-UI event hub (SSE fan-out).
//
//  NOTE:
//  - When a DB-backed IStateStore is configured, session list can be restored.
// ============================================================

public sealed class ResearchSessionManager
{
    private readonly ConcurrentDictionary<string, ResearchSession> _sessions = new(StringComparer.Ordinal);
    private readonly SessionRuntime _runtime;
    private readonly SessionUiTraceRecorder _uiTrace;
    private readonly IVibeSessionStore? _sessionStore;
    private readonly IStateStore<VibeSessionIndex>? _indexStore;
    private readonly ILogger<ResearchSessionManager>? _logger;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private const string IndexKey = "vibe_researching_sessions_index";

    public ResearchSessionManager(
        SessionRuntime runtime,
        SessionUiTraceRecorder uiTrace,
        IVibeSessionStore? sessionStore = null,
        IStateStore<VibeSessionIndex>? indexStore = null,
        ILogger<ResearchSessionManager>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _uiTrace = uiTrace ?? throw new ArgumentNullException(nameof(uiTrace));
        _sessionStore = sessionStore;
        _indexStore = indexStore;
        _logger = logger;
    }

    public IReadOnlyList<object> ListSessions()
    {
        return _sessions.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                sessionId = s.Id,
                createdAt = s.CreatedAt.ToString("O"),
                providerName = s.ProviderName
            })
            .ToList();
    }

    public ResearchSession Create(string? providerName)
    {
        return CreateAsync(providerName).GetAwaiter().GetResult();
    }

    public async Task<ResearchSession> CreateAsync(
        string? providerName,
        CancellationToken ct = default)
    {
        var state = await _runtime.CreateSessionAsync(workflowName: null, sessionId: null, ct);
        var createdAt = TryReadTimestamp(state.CreatedAt);
        var session = GetOrCreate(state.SessionId, providerName, createdAt);
        await PersistSessionAsync(session, ct);
        return session;
    }

    public bool TryGet(string sessionId, out ResearchSession session)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
        {
            session = null!;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out session!);
    }

    public async Task LoadPersistedSessionsAsync(CancellationToken ct = default)
    {
        try
        {
            var records = await LoadPersistedRecordsAsync(ct);
            if (records.Count > 0)
            {
                foreach (var record in records)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(record.SessionId))
                        continue;

                    await EnsureRuntimeSessionAsync(record.SessionId, ct);

                    var providerName = NormalizeOptional(record.ProviderName);
                    var dagId = NormalizeOptional(record.DagId);
                    var createdAt = TryReadTimestamp(record.CreatedAt);

                    GetOrCreate(record.SessionId, providerName, createdAt, dagId);
                }

                return;
            }

            var list = await _runtime.ListSessionsAsync(limit: 2000, ct);
            foreach (var summary in list)
            {
                ct.ThrowIfCancellationRequested();
                await EnsureRuntimeSessionAsync(summary.SessionId, ct);
                GetOrCreate(summary.SessionId, null, summary.CreatedAt);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load persisted sessions index.");
        }
    }

    public async Task PersistSessionAsync(ResearchSession session, CancellationToken ct = default)
    {
        try
        {
            if (_sessionStore != null)
            {
                await _sessionStore.SaveAsync(BuildRecord(session), ct);
                return;
            }

            if (_indexStore != null)
            {
                await UpsertIndexEntryAsync(session, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist session index for {SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Get an existing session or create a new in-memory session with the specified id.
    ///
    /// Why:
    /// - Sessions are ephemeral in this MVP (in-memory only).
    /// - Tool calls may arrive after restart, or from UI that only persisted File-SSoT workspace.
    /// - We still want to allow edits (DAG/plan/mesh) for an existing sessionId.
    /// </summary>
    public ResearchSession GetOrCreate(
        string sessionId,
        string? providerName = null,
        DateTimeOffset? createdAt = null,
        string? dagId = null)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        // Keep ids filesystem-safe and stable.
        if (sessionId.Length > 64)
            sessionId = sessionId[..64];

        // Normalize to lowercase and strip non-alnum (reject if unsafe).
        var normalized = sessionId.ToLowerInvariant();
        foreach (var ch in normalized)
        {
            if (!char.IsLetterOrDigit(ch))
                throw new ArgumentException("sessionId must be alphanumeric", nameof(sessionId));
        }

        var p = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim();
        var d = string.IsNullOrWhiteSpace(dagId) ? null : dagId.Trim();

        var stream = EnsureStream(normalized);
        var session = _sessions.GetOrAdd(normalized, id =>
        {
            var s = new ResearchSession(id, stream, createdAt) { ProviderName = p, DagId = d };
            _uiTrace.Attach(s);
            return s;
        });

        if (session.DagId == null && !string.IsNullOrWhiteSpace(d))
            session.DagId = d;

        return session;
    }

    private async Task<VibeSessionIndex> LoadIndexAsync(CancellationToken ct)
    {
        if (_indexStore == null)
            return new VibeSessionIndex();

        var loaded = await _indexStore.LoadAsync(IndexKey, ct);
        return loaded ?? new VibeSessionIndex();
    }

    private async Task SaveIndexAsync(VibeSessionIndex index, CancellationToken ct)
    {
        if (_indexStore == null)
            return;

        await _indexStore.SaveAsync(IndexKey, index, ct);
    }

    private async Task UpsertIndexEntryAsync(ResearchSession session, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var existing = index.Sessions
                .FirstOrDefault(s => string.Equals(s.SessionId, session.Id, StringComparison.Ordinal));

            if (existing == null)
            {
                index.Sessions.Add(BuildRecord(session));
            }
            else
            {
                var updated = BuildRecord(session);
                existing.ProviderName = updated.ProviderName;
                existing.DagId = updated.DagId;
                existing.CreatedAt = updated.CreatedAt;
                existing.UpdatedAt = updated.UpdatedAt;
            }

            await SaveIndexAsync(index, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<IReadOnlyList<VibeSessionRecord>> LoadPersistedRecordsAsync(CancellationToken ct)
    {
        if (_sessionStore != null)
            return await _sessionStore.ListAsync(ct);

        if (_indexStore == null)
            return Array.Empty<VibeSessionRecord>();

        var index = await LoadIndexAsync(ct);
        return index.Sessions;
    }

    private static VibeSessionRecord BuildRecord(ResearchSession session)
    {
        return new VibeSessionRecord
        {
            SessionId = session.Id,
            ProviderName = session.ProviderName ?? string.Empty,
            DagId = session.DagId ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(session.CreatedAt.UtcDateTime),
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    private static DateTimeOffset? TryReadTimestamp(Timestamp ts)
    {
        if (ts == null || (ts.Seconds == 0 && ts.Nanos == 0))
            return null;

        try
        {
            return ts.ToDateTimeOffset();
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private SessionAgUiStream EnsureStream(string sessionId)
    {
        try
        {
            var stream = _runtime.GetSessionStreamAsync(sessionId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (stream != null)
                return stream;

            _runtime.CreateSessionAsync(workflowName: null, sessionId: sessionId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            stream = _runtime.GetSessionStreamAsync(sessionId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (stream != null)
                return stream;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to bind session stream for {SessionId}", sessionId);
        }

        throw new InvalidOperationException("Failed to bind SessionAgUiStream.");
    }

    private async Task EnsureRuntimeSessionAsync(string sessionId, CancellationToken ct)
    {
        var stream = await _runtime.GetSessionStreamAsync(sessionId, ct);
        if (stream != null)
            return;

        await _runtime.CreateSessionAsync(workflowName: null, sessionId: sessionId, ct);
    }
}

public sealed class ResearchSession(string id, SessionAgUiStream events, DateTimeOffset? createdAt = null)
{
    // ============================================================
    //  Global Knowledge Graph
    //
    //  All sessions share a single global KnowledgeGraph by default.
    //  This enables:
    //  - Cross-session knowledge node visibility
    //  - Cross-session knowledge node connections
    //  - Research in any session can build upon knowledge from other sessions
    // ============================================================
    public const string GlobalDagId = "global";

    public string Id { get; } = id;
    public DateTimeOffset CreatedAt { get; } = createdAt ?? DateTimeOffset.UtcNow;
    public string? ProviderName { get; init; }

    // ------------------------------------------------------------
    // DAG binding
    //
    // 中文说明：
    // - 默认：所有 session 共享全局 KnowledgeGraph（dagId == "global"）
    // - 这样所有 session 的知识节点可以互相连接、互相引用
    // - 如需隔离，可手动设置 DagId = sessionId
    // ------------------------------------------------------------
    public string? DagId { get; set; }

    /// <summary>
    /// Get the effective DAG ID for this session.
    /// Defaults to GlobalDagId for cross-session knowledge sharing.
    /// </summary>
    public string EffectiveDagId => string.IsNullOrWhiteSpace(DagId) ? GlobalDagId : DagId.Trim();

    public SessionAgUiStream Events { get; } = events ?? throw new ArgumentNullException(nameof(events));

    // Lightweight server-side workspace state (rendered via AG-UI STATE_SNAPSHOT/DELTA).
    public ResearchWorkspaceState Workspace { get; } = new()
    {
        SessionId = id
    };

    // ------------------------------------------------------------
    // Message log (snapshot-first reconnect)
    //
    // Why:
    // - Multi-agent runs may not write into a single agent's State.History.
    // - We keep a small server-side canonical message log so reconnect always works.
    // ------------------------------------------------------------
    private readonly object _messagesLock = new();
    private readonly List<AgUiMessage> _messages = new();
    private readonly Dictionary<string, int> _messageIndex = new(StringComparer.Ordinal);

    // Serialize chat runs per session (avoid concurrent tool loops / history corruption).
    public SemaphoreSlim RunLock { get; } = new(1, 1);

    private int _runSeq;

    public int NextRunSeq() => Interlocked.Increment(ref _runSeq);

    // ------------------------------------------------------------
    // Interruptible runs (Latest-wins, session scope)
    //
    // 中文说明：
    // - 新输入到来时，取消当前 active run（协作式），并立即开始新 run。
    // - 不做 hard kill；仅依赖 CancellationToken + 下游检查。
    // - 不在 cancel 时 Dispose old RunContext（避免 CTS 过早释放导致回调异常）。
    //   old run 在其 Task 结束时自行 Dispose。
    // ------------------------------------------------------------
    private readonly object _runGate = new();
    public RunContext? ActiveRun { get; private set; }

    public RunContext BeginNewRun(string runId, string? reason, out string? interruptedRunId)
    {
        runId = (runId ?? string.Empty).Trim();
        if (runId.Length == 0) throw new ArgumentException("runId is required", nameof(runId));

        RunContext? old;
        var next = new RunContext(scopeId: Id, runId: runId);

        lock (_runGate)
        {
            old = ActiveRun;
            ActiveRun = next;
        }

        if (old != null)
        {
            interruptedRunId = old.RunId;
            old.MarkSuperseded(runId, reason);
            old.Cancel();
        }
        else
        {
            interruptedRunId = null;
        }

        return next;
    }

    public bool TryClearActiveRun(string runId, RunContext context)
    {
        runId = (runId ?? string.Empty).Trim();
        if (runId.Length == 0) return false;

        lock (_runGate)
        {
            if (ActiveRun == null) return false;
            if (!ReferenceEquals(ActiveRun, context)) return false;
            if (!string.Equals(ActiveRun.RunId, runId, StringComparison.Ordinal)) return false;

            ActiveRun = null;
            return true;
        }
    }

    public List<AgUiMessage> GetMessagesSnapshot(int maxMessages)
    {
        maxMessages = Math.Clamp(maxMessages, 0, 200);
        if (maxMessages == 0) return [];

        lock (_messagesLock)
        {
            if (_messages.Count == 0) return [];
            var take = Math.Min(maxMessages, _messages.Count);
            return _messages
                .Skip(Math.Max(0, _messages.Count - take))
                .Select(m => new AgUiMessage
                {
                    Id = m.Id,
                    Role = m.Role,
                    Content = m.Content,
                    Name = m.Name,
                    ToolCallId = m.ToolCallId
                })
                .ToList();
        }
    }

    public void SetMessage(string messageId, string role, string content)
    {
        messageId = (messageId ?? string.Empty).Trim();
        role = (role ?? string.Empty).Trim();
        content ??= string.Empty;

        if (messageId.Length == 0 || role.Length == 0)
            return;

        lock (_messagesLock)
        {
            if (_messageIndex.TryGetValue(messageId, out var idx))
            {
                var existing = _messages[idx];
                _messages[idx] = existing with { Role = role, Content = content };
            }
            else
            {
                _messageIndex[messageId] = _messages.Count;
                _messages.Add(new AgUiMessage { Id = messageId, Role = role, Content = content });
            }
        }
    }

    public void AppendToMessage(string messageId, string role, string delta)
    {
        messageId = (messageId ?? string.Empty).Trim();
        role = (role ?? string.Empty).Trim();
        delta ??= string.Empty;

        if (messageId.Length == 0 || role.Length == 0 || delta.Length == 0)
            return;

        lock (_messagesLock)
        {
            if (_messageIndex.TryGetValue(messageId, out var idx))
            {
                var existing = _messages[idx];
                _messages[idx] = existing with { Role = role, Content = (existing.Content ?? string.Empty) + delta };
            }
            else
            {
                _messageIndex[messageId] = _messages.Count;
                _messages.Add(new AgUiMessage { Id = messageId, Role = role, Content = delta });
            }
        }
    }

    // ------------------------------------------------------------
    // Interruption context (for direction change handling)
    //
    // 中文说明：
    // - 当用户发送新消息中断当前 run 时，记录中断上下文
    // - 新 run 可以读取并处理此上下文以执行方向变更等操作
    // ------------------------------------------------------------
    private readonly object _interruptionLock = new();
    private InterruptionContext? _lastInterruption;

    /// <summary>
    /// Records an interruption context for the new run to process.
    /// </summary>
    internal void RecordInterruption(InterruptionContext ctx)
    {
        lock (_interruptionLock)
        {
            _lastInterruption = ctx;
        }
    }

    /// <summary>
    /// Gets the last interruption context without consuming it.
    /// </summary>
    internal InterruptionContext? GetLastInterruption()
    {
        lock (_interruptionLock)
        {
            return _lastInterruption;
        }
    }

    /// <summary>
    /// Consumes and returns the last interruption context (clears it after reading).
    /// </summary>
    internal InterruptionContext? ConsumeLastInterruption()
    {
        lock (_interruptionLock)
        {
            var ctx = _lastInterruption;
            _lastInterruption = null;
            return ctx;
        }
    }
}


