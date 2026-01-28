using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Core.Runtime;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.Sessions.Constants;
using Aevatar.VibeResearching.Agents;

namespace Aevatar.VibeResearching.Sessions.Services;

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
    private readonly ISessionUiTraceService _uiTrace;
    private readonly IVibeSessionRepository? _sessionRepository;
    private readonly IStateStore<VibeSessionIndex>? _indexStore;
    private readonly ILogger<ResearchSessionManager>? _logger;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public ResearchSessionManager(
        ISessionUiTraceService uiTrace,
        IVibeSessionRepository? sessionRepository = null,
        IStateStore<VibeSessionIndex>? indexStore = null,
        ILogger<ResearchSessionManager>? logger = null)
    {
        _uiTrace = uiTrace ?? throw new ArgumentNullException(nameof(uiTrace));
        _sessionRepository = sessionRepository;
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
        // Use full GUID (N) to avoid collisions and match other File-SSoT ids.
        var id = Guid.NewGuid().ToString("N");
        var session = GetOrCreate(id, providerName);
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
            if (records.Count == 0)
                return;

            foreach (var record in records)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(record.SessionId))
                    continue;

                var providerName = NormalizeOptional(record.ProviderName);
                var dagId = NormalizeOptional(record.DagId);
                var createdAt = TryReadTimestamp(record.CreatedAt);

                GetOrCreate(record.SessionId, providerName, createdAt, dagId);
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
            if (_sessionRepository != null)
            {
                await _sessionRepository.SaveAsync(BuildRecord(session), ct);
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
        if (sessionId.Length > SessionConsts.MaxSessionIdLength)
            sessionId = sessionId[..SessionConsts.MaxSessionIdLength];

        // Normalize to lowercase and strip non-alnum (reject if unsafe).
        var normalized = sessionId.ToLowerInvariant();
        foreach (var ch in normalized)
        {
            if (!char.IsLetterOrDigit(ch))
                throw new ArgumentException("sessionId must be alphanumeric", nameof(sessionId));
        }

        var p = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim();
        var d = string.IsNullOrWhiteSpace(dagId) ? null : dagId.Trim();

        var session = _sessions.GetOrAdd(normalized, id =>
        {
            var s = new ResearchSession(id, createdAt) { ProviderName = p, DagId = d };
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

        var loaded = await _indexStore.LoadAsync(SessionConsts.SessionIndexKey, ct);
        return loaded ?? new VibeSessionIndex();
    }

    private async Task SaveIndexAsync(VibeSessionIndex index, CancellationToken ct)
    {
        if (_indexStore == null)
            return;

        await _indexStore.SaveAsync(SessionConsts.SessionIndexKey, index, ct);
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
        if (_sessionRepository != null)
            return await _sessionRepository.ListAsync(ct);

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

    /// <summary>
    /// Creates a new session (alias for CreateAsync that returns VibeSessionRecord).
    /// </summary>
    public async Task<VibeSessionRecord> CreateSessionAsync(string? providerName, CancellationToken ct = default)
    {
        var session = await CreateAsync(providerName, ct);
        return BuildRecord(session);
    }

    /// <summary>
    /// Deletes a session and cleans up resources.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            // Remove from persistent store if available
            if (_sessionRepository != null)
            {
                // Note: IVibeSessionRepository doesn't have DeleteAsync, so we just remove from memory
                _logger?.LogInformation("Deleted session {SessionId} from memory", sessionId);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Prepares a new run ID for input processing.
    /// The actual execution wiring happens at the Application/API layer.
    /// </summary>
    public string PrepareRunId(string sessionId)
    {
        if (!TryGet(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session '{sessionId}' not found");
        }

        var runSeq = session.NextRunSeq();
        var runId = $"{session.Id}:{runSeq}";

        _logger?.LogInformation("Prepared run ID {RunId} for session {SessionId}", runId, sessionId);
        return runId;
    }

    /// <summary>
    /// Reconnects MCP (Model Context Protocol) for a session.
    /// </summary>
    public async Task ReconnectMcpAsync(string sessionId, CancellationToken ct = default)
    {
        if (TryGet(sessionId, out var session))
        {
            // Note: Actual MCP reconnection logic would go here
            _logger?.LogInformation("Reconnecting MCP for session {SessionId}", sessionId);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets a snapshot of available tools for a session.
    /// </summary>
    public async Task<object> GetToolsSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        if (TryGet(sessionId, out var session))
        {
            // Return a placeholder tools snapshot
            return new
            {
                sessionId = session.Id,
                tools = Array.Empty<object>()
            };
        }

        return new
        {
            sessionId,
            tools = Array.Empty<object>()
        };
    }
}

public sealed class ResearchSession(string id, DateTimeOffset? createdAt = null)
{
    private const string EventsHubName = "ResearchSession.Events";

    public const string GlobalDagId = SessionConsts.GlobalDagId;

    public string Id { get; } = id;
    public DateTimeOffset CreatedAt { get; } = createdAt ?? DateTimeOffset.UtcNow;
    public string? ProviderName { get; init; }

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

    // Message log for snapshot-first reconnect
    private readonly object _messagesLock = new();
    private readonly List<AgUiMessage> _messages = new();
    private readonly Dictionary<string, int> _messageIndex = new(StringComparer.Ordinal);

    // Serialize chat runs per session (avoid concurrent tool loops / history corruption).
    public SemaphoreSlim RunLock { get; } = new(1, 1);

    private int _runSeq;

    public int NextRunSeq() => Interlocked.Increment(ref _runSeq);

    // Interruptible runs (Latest-wins, session scope)
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
        maxMessages = Math.Clamp(maxMessages, 0, SessionConsts.MaxMessagesSnapshot);
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

    // Interruption context
    private readonly object _interruptionLock = new();
    private InterruptionContext? _lastInterruption;

    public void RecordInterruption(InterruptionContext ctx)
    {
        lock (_interruptionLock)
        {
            _lastInterruption = ctx;
        }
    }

    public InterruptionContext? GetLastInterruption()
    {
        lock (_interruptionLock)
        {
            return _lastInterruption;
        }
    }

    public InterruptionContext? ConsumeLastInterruption()
    {
        lock (_interruptionLock)
        {
            var ctx = _lastInterruption;
            _lastInterruption = null;
            return ctx;
        }
    }
}
