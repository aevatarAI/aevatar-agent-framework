using System.Collections.Concurrent;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Streaming;

namespace Aevatar.Notebook.Api.Sessions;

// ============================================================
//  NotebookSessionManager
//
//  - Maintains in-memory session registry (MVP).
//  - Each session owns an AG-UI event hub (SSE fan-out).
//
//  NOTE:
//  - Sessions are ephemeral in this MVP. Persistent sessions can be added later
//    by projecting to a store (but keep protocol stable).
// ============================================================

public sealed class NotebookSessionManager
{
    private readonly ConcurrentDictionary<string, NotebookSession> _sessions = new(StringComparer.Ordinal);

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

    public NotebookSession Create(string? providerName)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var session = new NotebookSession(id)
        {
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim()
        };
        _sessions[id] = session;
        return session;
    }

    public bool TryGet(string sessionId, out NotebookSession session)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
        {
            session = null!;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out session!);
    }
}

public sealed class NotebookSession(string id)
{
    private const string EventsHubName = "NotebookSession.Events";
    public string Id { get; } = id;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public string? ProviderName { get; init; }

    public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0, hubName: EventsHubName);

    // Serialize chat runs per session (avoid concurrent tool loops / history corruption).
    public SemaphoreSlim RunLock { get; } = new(1, 1);

    private int _runSeq;

    public int NextRunSeq() => Interlocked.Increment(ref _runSeq);
}


