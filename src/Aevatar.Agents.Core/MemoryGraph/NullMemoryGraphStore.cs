using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Core.MemoryGraphs;

/// <summary>
/// No-op memory graph store (best-effort fallback).
/// </summary>
public sealed class NullMemoryGraphStore : IMemoryGraphStore
{
    public static readonly NullMemoryGraphStore Instance = new();

    private NullMemoryGraphStore()
    {
    }

    public Task SaveAsync(MemoryGraph graph, CancellationToken ct = default) => Task.CompletedTask;

    public Task<MemoryGraph?> LoadAsync(string graphId, CancellationToken ct = default)
        => Task.FromResult<MemoryGraph?>(null);

    public Task<bool> ExistsAsync(string graphId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task DeleteAsync(string graphId, CancellationToken ct = default)
        => Task.CompletedTask;
}


