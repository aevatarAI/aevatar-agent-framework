using System.Collections.Concurrent;
using Aevatar.Agents.AGUI;
using ScientificResearchAssistant.Api.Infrastructure;

namespace ScientificResearchAssistant.Api.Sessions;

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
        var id = Guid.NewGuid().ToString("N")[..12];
        var session = new ResearchSession(id)
        {
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim()
        };
        _sessions[id] = session;
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
}

public sealed class ResearchSession(string id)
{
    public string Id { get; } = id;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public string? ProviderName { get; init; }

    public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0);

    // Serialize chat runs per session (avoid concurrent tool loops / history corruption).
    public SemaphoreSlim RunLock { get; } = new(1, 1);

    private int _runSeq;

    public int NextRunSeq() => Interlocked.Increment(ref _runSeq);
}


