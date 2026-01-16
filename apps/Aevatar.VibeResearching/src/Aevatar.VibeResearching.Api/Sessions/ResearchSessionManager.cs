using System.Collections.Concurrent;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Core.Runtime;

namespace VibeResearching.Api.Sessions;

// ============================================================
//  ResearchSessionManager (MVP)
//
//  - In-memory session registry.
//  - Each session owns an AG-UI event hub (SSE fan-out).
//
//  NOTE:
//  - Sessions are ephemeral in this MVP.
//  - Persistence can be added later, keep the protocol stable.
// ============================================================

public sealed class ResearchSessionManager
{
    private readonly ConcurrentDictionary<string, ResearchSession> _sessions = new(StringComparer.Ordinal);
    private readonly SessionUiTraceRecorder _uiTrace;

    public ResearchSessionManager(SessionUiTraceRecorder uiTrace)
    {
        _uiTrace = uiTrace ?? throw new ArgumentNullException(nameof(uiTrace));
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
        // Use full GUID (N) to avoid collisions and match other File-SSoT ids.
        var id = Guid.NewGuid().ToString("N");
        return GetOrCreate(id, providerName);
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

    /// <summary>
    /// Get an existing session or create a new in-memory session with the specified id.
    ///
    /// Why:
    /// - Sessions are ephemeral in this MVP (in-memory only).
    /// - Tool calls may arrive after restart, or from UI that only persisted File-SSoT workspace.
    /// - We still want to allow edits (DAG/plan/mesh) for an existing sessionId.
    /// </summary>
    public ResearchSession GetOrCreate(string sessionId, string? providerName = null)
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

        return _sessions.GetOrAdd(normalized, id =>
        {
            var s = new ResearchSession(id) { ProviderName = p };
            _uiTrace.Attach(s);
            return s;
        });
    }
}

public sealed class ResearchSession(string id)
{
    private const string EventsHubName = "ResearchSession.Events";
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
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
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

    public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0, hubName: EventsHubName);

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
}


