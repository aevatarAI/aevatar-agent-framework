using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Core.Runtime;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.Sessions.Constants;
using Aevatar.VibeResearching.Sessions.Enums;
using Aevatar.VibeResearching.Agents;
using ProtoSessionStatus = Aevatar.VibeResearching.Agents.Contracts.Sessions.SessionStatus;
using VibeSessionRecord = Aevatar.VibeResearching.Agents.Contracts.Sessions.VibeSessionRecord;
using VibeSessionIndex = Aevatar.VibeResearching.Agents.Contracts.Sessions.VibeSessionIndex;

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

    public IReadOnlyList<object> ListSessions(bool includeArchived = false)
    {
        return _sessions.Values
            .Where(s => includeArchived || s.Status != SessionStatus.Archived)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                sessionId = s.Id,
                createdAt = s.CreatedAt.ToString("O"),
                providerName = s.ProviderName,
                status = s.Status.ToString().ToLowerInvariant()
            })
            .ToList();
    }

    public ResearchSession Create(string? providerName)
    {
        return CreateAsync(providerName).GetAwaiter().GetResult();
    }

    public async Task<ResearchSession> CreateAsync(
        string? providerName,
        string? ownerId = null,
        CancellationToken ct = default)
    {
        // Use full GUID (N) to avoid collisions and match other File-SSoT ids.
        var id = Guid.NewGuid().ToString("N");
        var session = GetOrCreate(id, providerName, ownerId: ownerId);
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
                var ownerId = NormalizeOptional(record.OwnerId);

                var session = GetOrCreate(record.SessionId, providerName, createdAt, dagId, ownerId);
                var restoredStatus = MapFromProtoStatus(record.Status);
                var restoredPausedAt = TryReadTimestamp(record.PausedAt);
                var restoredArchivedAt = TryReadTimestamp(record.ArchivedAt);
                session.RestoreStatus(restoredStatus, restoredPausedAt, restoredArchivedAt);
                session.OwnerName = NormalizeOptional(record.OwnerName);
                session.LastUserMessage = NormalizeOptional(record.LastUserMessage);
                session.LastRunMode = NormalizeOptional(record.LastRunMode);
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
        string? dagId = null,
        string? ownerId = null)
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
        var o = string.IsNullOrWhiteSpace(ownerId) ? null : ownerId.Trim();

        var session = _sessions.GetOrAdd(normalized, id =>
        {
            var s = new ResearchSession(id, createdAt) { ProviderName = p, DagId = d, OwnerId = o };
            _uiTrace.Attach(s);
            return s;
        });

        if (session.DagId == null && !string.IsNullOrWhiteSpace(d))
            session.DagId = d;

        if (session.OwnerId == null && !string.IsNullOrWhiteSpace(o))
            session.OwnerId = o;

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
                existing.OwnerId = updated.OwnerId;
                existing.OwnerName = updated.OwnerName;
                existing.Status = updated.Status;
                existing.PausedAt = updated.PausedAt;
                existing.ArchivedAt = updated.ArchivedAt;
                existing.LastUserMessage = updated.LastUserMessage;
                existing.LastRunMode = updated.LastRunMode;
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
        var record = new VibeSessionRecord
        {
            SessionId = session.Id,
            ProviderName = session.ProviderName ?? string.Empty,
            DagId = session.DagId ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(session.CreatedAt.UtcDateTime),
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            OwnerId = session.OwnerId ?? string.Empty,
            OwnerName = session.OwnerName ?? string.Empty,
            Status = MapToProtoStatus(session.Status)
        };

        if (session.PausedAt.HasValue)
            record.PausedAt = Timestamp.FromDateTime(session.PausedAt.Value.UtcDateTime);
        if (session.ArchivedAt.HasValue)
            record.ArchivedAt = Timestamp.FromDateTime(session.ArchivedAt.Value.UtcDateTime);

        record.LastUserMessage = session.LastUserMessage ?? string.Empty;
        record.LastRunMode = session.LastRunMode ?? string.Empty;

        return record;
    }

    private static ProtoSessionStatus MapToProtoStatus(SessionStatus status)
    {
        return status switch
        {
            SessionStatus.Active => ProtoSessionStatus.Active,
            SessionStatus.Paused => ProtoSessionStatus.Paused,
            SessionStatus.Archived => ProtoSessionStatus.Archived,
            _ => ProtoSessionStatus.Active
        };
    }

    private static SessionStatus MapFromProtoStatus(ProtoSessionStatus status)
    {
        return status switch
        {
            ProtoSessionStatus.Paused => SessionStatus.Paused,
            ProtoSessionStatus.Archived => SessionStatus.Archived,
            _ => SessionStatus.Active // UNSPECIFIED and ACTIVE both map to Active
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
    /// Returns sessions that are Active but have no active run and were last updated more than staleThreshold ago.
    /// These are candidates for auto-pause.
    /// </summary>
    public IReadOnlyList<ResearchSession> GetStaleResumableSessions(TimeSpan staleThreshold)
    {
        var cutoff = DateTimeOffset.UtcNow - staleThreshold;
        return _sessions.Values
            .Where(s => s.Status == SessionStatus.Active && s.ActiveRun == null && s.LastActivityAt < cutoff)
            .ToList();
    }

    /// <summary>
    /// Returns all Active sessions that have no in-memory active run.
    /// Used on startup to pause sessions orphaned by a previous process crash.
    /// </summary>
    public IReadOnlyList<ResearchSession> GetOrphanedActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.Status == SessionStatus.Active && s.ActiveRun == null)
            .ToList();
    }

    /// <summary>
    /// Pauses a session by ID. Validates state and persists the change.
    /// </summary>
    public async Task PauseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        session.Pause();
        await PersistSessionAsync(session, ct);
        _logger?.LogInformation("Session {SessionId} paused", sessionId);
    }

    /// <summary>
    /// Resumes a paused session by ID. Validates state and persists the change.
    /// </summary>
    public async Task ResumeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        session.Resume();
        await PersistSessionAsync(session, ct);
        _logger?.LogInformation("Session {SessionId} resumed", sessionId);
    }

    /// <summary>
    /// Terminates (archives) a session by ID. Validates state and persists the change.
    /// </summary>
    public async Task TerminateSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!TryGet(sessionId, out var session))
            throw new InvalidOperationException($"Session '{sessionId}' not found");

        session.Terminate();
        await PersistSessionAsync(session, ct);
        _logger?.LogInformation("Session {SessionId} terminated (archived)", sessionId);
    }

    /// <summary>
    /// Creates a new session (alias for CreateAsync that returns VibeSessionRecord).
    /// </summary>
    public async Task<VibeSessionRecord> CreateSessionAsync(string? providerName, string? ownerId = null, string? ownerName = null, CancellationToken ct = default)
    {
        var session = await CreateAsync(providerName, ownerId, ct);
        session.OwnerName = ownerName;
        // Persist again so OwnerName is saved (CreateAsync persists before OwnerName is set)
        await PersistSessionAsync(session, ct);
        return BuildRecord(session);
    }

    /// <summary>
    /// Deletes a session and cleans up resources.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);

        if (_sessionRepository != null)
        {
            await _sessionRepository.DeleteAsync(sessionId, ct);
            _logger?.LogInformation("Deleted session {SessionId} from memory and persistent storage", sessionId);
        }
        else
        {
            _logger?.LogInformation("Deleted session {SessionId} from memory (no persistent store configured)", sessionId);
        }
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

    /// <summary>
    /// Tracks the last time this session had meaningful activity (input, run start, etc.).
    /// Used by stale-session detection. Defaults to CreatedAt.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; } = createdAt ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// The user ID of the session creator. Null/empty for legacy or anonymous sessions.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// The display name of the session creator.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// The lifecycle status of the session. Defaults to Active.
    /// </summary>
    public SessionStatus Status { get; private set; } = SessionStatus.Active;

    /// <summary>
    /// Timestamp when the session was paused. Null if not paused.
    /// </summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>
    /// Timestamp when the session was archived (terminated). Null if not archived.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>
    /// Last user message text, saved for resume context.
    /// </summary>
    public string? LastUserMessage { get; set; }

    /// <summary>
    /// Last input mode used (chat/vibe/vibe_loop).
    /// </summary>
    public string? LastRunMode { get; set; }

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

    /// <summary>
    /// Pauses the session. Only valid when Status is Active.
    /// Cancels any active run and emits a session_paused SSE event.
    /// </summary>
    public void Pause()
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException($"Cannot pause session in '{Status}' state. Only Active sessions can be paused.");

        CancelActiveRunInternal();
        Status = SessionStatus.Paused;
        PausedAt = DateTimeOffset.UtcNow;

        Events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "session_paused",
            Value = new { sessionId = Id, pausedAt = PausedAt?.ToString("O") }
        });
    }

    /// <summary>
    /// Resumes the session. Only valid when Status is Paused.
    /// Emits a session_resumed SSE event.
    /// </summary>
    public void Resume()
    {
        if (Status != SessionStatus.Paused)
            throw new InvalidOperationException($"Cannot resume session in '{Status}' state. Only Paused sessions can be resumed.");

        Status = SessionStatus.Active;
        PausedAt = null;

        Events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "session_resumed",
            Value = new { sessionId = Id }
        });
    }

    /// <summary>
    /// Terminates (archives) the session. Valid when Status is Active or Paused.
    /// Cancels any active run and emits a session_terminated SSE event, then completes the event stream.
    /// </summary>
    public void Terminate()
    {
        if (Status == SessionStatus.Archived)
            throw new InvalidOperationException("Cannot terminate session that is already archived.");

        CancelActiveRunInternal();
        Status = SessionStatus.Archived;
        ArchivedAt = DateTimeOffset.UtcNow;

        Events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "session_terminated",
            Value = new { sessionId = Id, archivedAt = ArchivedAt?.ToString("O") }
        });

        Events.Complete();
    }

    /// <summary>
    /// Ensures the session is in a state that accepts input (Active).
    /// Throws InvalidOperationException if the session is Paused or Archived.
    /// </summary>
    public void EnsureAcceptsInput()
    {
        if (Status == SessionStatus.Paused)
            throw new InvalidOperationException("Session is paused");
        if (Status == SessionStatus.Archived)
            throw new InvalidOperationException("Session is archived");
    }

    /// <summary>
    /// Restores the session status from a persisted proto record.
    /// Treats UNSPECIFIED as Active for backward compatibility.
    /// </summary>
    public void RestoreStatus(SessionStatus status, DateTimeOffset? pausedAt, DateTimeOffset? archivedAt)
    {
        Status = status == 0 ? SessionStatus.Active : status;
        PausedAt = pausedAt;
        ArchivedAt = archivedAt;
    }

    private void CancelActiveRunInternal()
    {
        lock (_runGate)
        {
            ActiveRun?.Cancel();
            ActiveRun = null;
        }
    }

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
