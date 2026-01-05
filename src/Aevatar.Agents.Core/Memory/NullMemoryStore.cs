using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Core.Memory;

/// <summary>
/// No-op memory store (best-effort fallback).
/// </summary>
public sealed class NullMemoryStore : IMemoryStore
{
    public static readonly NullMemoryStore Instance = new();

    private NullMemoryStore()
    {
    }

    public Task AppendAsync(MemoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemoryResourceSummary>>([]);

    public Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemoryEntry>>([]);
}


