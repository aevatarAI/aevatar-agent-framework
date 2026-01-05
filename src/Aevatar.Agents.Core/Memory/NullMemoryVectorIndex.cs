using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Core.Memory;

/// <summary>
/// No-op vector index (best-effort fallback).
/// </summary>
public sealed class NullMemoryVectorIndex : IMemoryVectorIndex
{
    public static readonly NullMemoryVectorIndex Instance = new();

    private NullMemoryVectorIndex()
    {
    }

    public Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int limit = 20,
        string? memoryId = null,
        MemoryScopeType? scopeTypeFilter = null,
        string? scopeId = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MemoryVectorMatch>>([]);
}


