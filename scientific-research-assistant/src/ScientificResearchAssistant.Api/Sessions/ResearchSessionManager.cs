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


