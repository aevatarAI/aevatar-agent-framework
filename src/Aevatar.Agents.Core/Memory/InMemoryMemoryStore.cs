using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Memory;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Memory;

// ============================================================
//  InMemoryMemoryStore
//
//  WHY:
//  - Fast, dependency-free default for dev/tests.
//  - Append-only semantics (per memory_id partition).
//
//  NOTE:
//  - This is NOT a vector index. Search is naive substring match (best-effort).
// ============================================================
public sealed class InMemoryMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MemoryEntry>> _byMemoryId = new(StringComparer.Ordinal);

    public Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);

        var memoryId = entry.MemoryId?.Trim();
        if (string.IsNullOrWhiteSpace(memoryId))
            throw new ArgumentException("memory_id is required.", nameof(entry));

        // Keep an immutable copy in store.
        var clone = entry.Clone();

        var q = _byMemoryId.GetOrAdd(memoryId, _ => new ConcurrentQueue<MemoryEntry>());
        q.Enqueue(clone);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var take = Math.Clamp(limit, 1, 2000);
        var list = new List<MemoryResourceSummary>();

        foreach (var kv in _byMemoryId)
        {
            ct.ThrowIfCancellationRequested();

            var memoryId = kv.Key;
            var items = kv.Value.ToArray();
            if (items.Length == 0) continue;

            var scope = items[0].Scope ?? new MemoryScope { Type = MemoryScopeType.Unspecified, ScopeId = string.Empty };
            if (scopeTypeFilter is not null &&
                scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                scope.Type != scopeTypeFilter.Value)
            {
                continue;
            }

            var latest = items[^1].CreatedAt ?? Timestamp.FromDateTime(DateTime.UtcNow);
            list.Add(new MemoryResourceSummary
            {
                MemoryId = memoryId,
                Scope = scope,
                EntryCount = items.Length,
                LatestAt = latest
            });
        }

        var ordered = list
            .OrderByDescending(r => r.LatestAt?.ToDateTime() ?? DateTime.MinValue)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryResourceSummary>>(ordered);
    }

    public Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(memoryId))
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        if (!_byMemoryId.TryGetValue(memoryId.Trim(), out var q))
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        var take = Math.Clamp(limit, 1, 2000);
        var items = q.ToArray();
        if (items.Length <= take)
            return Task.FromResult<IReadOnlyList<MemoryEntry>>(items);

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(items[^take..]);
    }

    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        var q = query.Trim();
        var take = Math.Clamp(limit, 1, 500);

        IEnumerable<KeyValuePair<string, ConcurrentQueue<MemoryEntry>>> partitions = _byMemoryId;
        if (!string.IsNullOrWhiteSpace(memoryId))
        {
            var id = memoryId.Trim();
            partitions = _byMemoryId.TryGetValue(id, out var only)
                ? new[] { new KeyValuePair<string, ConcurrentQueue<MemoryEntry>>(id, only) }
                : [];
        }

        var matches = new List<MemoryEntry>();
        foreach (var kv in partitions)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var e in kv.Value.ToArray())
            {
                ct.ThrowIfCancellationRequested();

                if (scopeTypeFilter is not null &&
                    scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                    (e.Scope?.Type ?? MemoryScopeType.Unspecified) != scopeTypeFilter.Value)
                {
                    continue;
                }

                if ((e.Content ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(e);
                }
            }
        }

        var ordered = matches
            .OrderByDescending(e => e.CreatedAt?.ToDateTime() ?? DateTime.MinValue)
            .Take(take)
            .Select(e => e.Clone())
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(ordered);
    }
}


