using System.Collections.Concurrent;
using AevatarKit;
using Google.Protobuf.WellKnownTypes;

namespace AevatarKit.Runtime.Memory;

public interface IMemoryStore
{
    void Append(MemoryEntry entry);
    IReadOnlyList<MemoryResourceSummary> ListResources(MemoryScopeType? scopeTypeFilter = null);
    IReadOnlyList<MemoryEntry> ListEntries(string memoryId, int limit = 200);
    IReadOnlyList<MemoryEntry> Search(string query, int limit = 50, MemoryScopeType? scopeTypeFilter = null);
}

/// <summary>
/// In-memory memory store for MVP.
///
/// Design:
/// - append-only entries
/// - per-memoryId partition
/// - naive search (substring) for MVP, later replaced by FTS/vector
/// </summary>
public sealed class InMemoryMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<MemoryEntry>> _byMemoryId = new();

    public void Append(MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.MemoryId))
        {
            throw new ArgumentException("memory_id is required");
        }

        var id = entry.MemoryId.Trim();
        var q = _byMemoryId.GetOrAdd(id, _ => new ConcurrentQueue<MemoryEntry>());
        q.Enqueue(entry);
    }

    public IReadOnlyList<MemoryResourceSummary> ListResources(MemoryScopeType? scopeTypeFilter = null)
    {
        var result = new List<MemoryResourceSummary>();

        foreach (var kv in _byMemoryId)
        {
            var memoryId = kv.Key;
            var items = kv.Value.ToArray();
            if (items.Length == 0) continue;

            var scope = items[0].Scope ?? new MemoryScope { Type = MemoryScopeType.Unspecified, ScopeId = "" };
            if (scopeTypeFilter is not null &&
                scopeTypeFilter.Value != MemoryScopeType.Unspecified &&
                scope.Type != scopeTypeFilter.Value)
            {
                continue;
            }

            var latest = items[^1].CreatedAt ?? Timestamp.FromDateTime(DateTime.UtcNow);
            result.Add(new MemoryResourceSummary
            {
                MemoryId = memoryId,
                Scope = scope,
                EntryCount = items.Length,
                LatestAt = latest
            });
        }

        return result
            .OrderByDescending(r => r.LatestAt?.ToDateTime() ?? DateTime.MinValue)
            .ToList();
    }

    public IReadOnlyList<MemoryEntry> ListEntries(string memoryId, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return Array.Empty<MemoryEntry>();
        }

        var id = memoryId.Trim();
        if (!_byMemoryId.TryGetValue(id, out var q))
        {
            return Array.Empty<MemoryEntry>();
        }

        var take = Math.Clamp(limit, 1, 2000);
        var items = q.ToArray();
        if (items.Length <= take) return items;
        return items[^take..];
    }

    public IReadOnlyList<MemoryEntry> Search(string query, int limit = 50, MemoryScopeType? scopeTypeFilter = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MemoryEntry>();
        }

        var q = query.Trim();
        var k = Math.Clamp(limit, 1, 200);

        var matches = new List<MemoryEntry>();

        foreach (var kv in _byMemoryId)
        {
            foreach (var e in kv.Value.ToArray())
            {
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

        return matches
            .OrderByDescending(e => e.CreatedAt?.ToDateTime() ?? DateTime.MinValue)
            .Take(k)
            .ToList();
    }
}


