using System.Collections.Concurrent;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf.WellKnownTypes;

namespace MemoryDemo;

// ============================================================
//  InMemoryAIMemory (Demo Only)
//
//  WHY:
//  - Enable "long-term memory" demos without requiring Mongo/Supabase.
//  - The framework injects IAevatarAIMemory via IAevatarAIMemoryFactory.
//  - This keeps the demo runnable out-of-the-box.
// ============================================================

public sealed class InMemoryAIMemoryFactory : IAevatarAIMemoryFactory
{
    private readonly ConcurrentDictionary<string, InMemoryAIMemory> _memories = new(StringComparer.Ordinal);

    public IAevatarAIMemory Create(string agentId, string? sessionId = null)
    {
        var key = string.IsNullOrWhiteSpace(sessionId) ? agentId : $"{agentId}::session::{sessionId}";
        return _memories.GetOrAdd(key, _ => new InMemoryAIMemory());
    }
}

public sealed class InMemoryAIMemory : IAevatarAIMemory
{
    private readonly object _lock = new();
    private readonly List<(string Role, string Content, DateTimeOffset Ts)> _items = new();

    public Task AddMessageAsync(string role, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(content))
            return Task.CompletedTask;

        lock (_lock)
        {
            _items.Add((Normalize(role), content, DateTimeOffset.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AevatarConversationEntry>> GetHistoryAsync(
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<(string Role, string Content, DateTimeOffset Ts)> copy;
        lock (_lock)
        {
            copy = _items.ToList();
        }

        var effectiveLimit = limit is > 0 ? limit.Value : 200;
        effectiveLimit = Math.Clamp(effectiveLimit, 1, 2000);

        if (copy.Count > effectiveLimit)
        {
            copy = copy.Skip(Math.Max(0, copy.Count - effectiveLimit)).ToList();
        }

        var result = new List<AevatarConversationEntry>(copy.Count);
        foreach (var x in copy)
        {
            result.Add(new AevatarConversationEntry
            {
                Role = x.Role,
                Content = x.Content,
                Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(x.Ts.UtcDateTime, DateTimeKind.Utc))
            });
        }

        return Task.FromResult<IReadOnlyList<AevatarConversationEntry>>(result);
    }

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _items.Clear();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var q = query.Trim();
        var k = Math.Clamp(topK, 1, 50);

        List<(string Role, string Content, DateTimeOffset Ts)> copy;
        lock (_lock)
        {
            copy = _items.ToList();
        }

        // Simple contains search (best-effort), keep latest matches first.
        var hits = copy
            .Where(x => (x.Content ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Ts)
            .Take(k)
            .Select(x => $"[{x.Role}] {x.Content}".Trim())
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(hits);
    }

    private static string Normalize(string role)
    {
        var r = (role ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(r) ? "unknown" : r;
    }
}


