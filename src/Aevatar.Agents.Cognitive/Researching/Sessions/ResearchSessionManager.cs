using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Core.Runtime;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Contracts.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Sessions;

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
    private readonly SessionUiTraceRecorder _uiTrace;
    private readonly IVibeSessionStore? _sessionStore;
    private readonly IStateStore<VibeSessionIndex>? _indexStore;
    private readonly ILogger<ResearchSessionManager>? _logger;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private const string IndexKey = "vibe_researching_sessions_index";

    public ResearchSessionManager(
        SessionUiTraceRecorder uiTrace,
        IVibeSessionStore? sessionStore = null,
        IStateStore<VibeSessionIndex>? indexStore = null,
        ILogger<ResearchSessionManager>? logger = null)
    {
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
            if (_sessionStore != null)
            {
                await _sessionStore.SaveAsync(BuildRecord(session), ct);
                return;
            }

            if (_indexStore == null)
                return;

            await _indexLock.WaitAsync(ct);
            try
            {
                var index = await LoadIndexAsync(ct);
                if (!index.Sessions.Any(x => string.Equals(x.SessionId, session.Id, StringComparison.Ordinal)))
                    index.Sessions.Add(BuildRecord(session));

                await SaveIndexAsync(index, ct);
            }
            finally
            {
                _indexLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist session index (best-effort).");
        }
    }

    public ResearchSession GetOrCreate(string sessionId, string? providerName, DateTimeOffset? createdAt = null, string? dagId = null)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        var session = _sessions.GetOrAdd(sessionId, id => new ResearchSession(id, createdAt)
        {
            ProviderName = providerName,
            DagId = dagId
        });

        // Attach UI trace recorder (best-effort).
        _uiTrace.Attach(session);
        return session;
    }

    // ============================================================
    //  Index persistence
    // ============================================================

    private async Task<VibeSessionIndex> LoadIndexAsync(CancellationToken ct)
    {
        if (_indexStore == null) return new VibeSessionIndex();
        var loaded = await _indexStore.LoadAsync(IndexKey, ct);
        return loaded ?? new VibeSessionIndex();
    }

    private async Task SaveIndexAsync(VibeSessionIndex index, CancellationToken ct)
    {
        if (_indexStore == null) return;
        await _indexStore.SaveAsync(IndexKey, index, ct);
    }

    private async Task<IReadOnlyList<VibeSessionRecord>> LoadPersistedRecordsAsync(CancellationToken ct)
    {
        if (_sessionStore == null && _indexStore == null)
            return Array.Empty<VibeSessionRecord>();

        if (_sessionStore != null)
            return await _sessionStore.ListAsync(ct);

        var index = await LoadIndexAsync(ct);
        return index.Sessions.ToList();
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
}
