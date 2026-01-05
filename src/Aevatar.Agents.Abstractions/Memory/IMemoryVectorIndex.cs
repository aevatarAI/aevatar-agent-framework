namespace Aevatar.Agents.Abstractions.Memory;

// ============================================================
//  Memory Vector Index (Abstraction)
//
//  WHY:
//  - Provide a DI boundary for persistent semantic retrieval.
//  - Default implementation can be file-based brute-force.
//  - Production can swap to pgvector / Elasticsearch dense_vector / Milvus, etc.
//
//  NOTE:
//  - This interface is process-local (DI). The stored record is Protobuf.
// ============================================================
public interface IMemoryVectorIndex
{
    Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int limit = 20,
        string? memoryId = null,
        MemoryScopeType? scopeTypeFilter = null,
        string? scopeId = null,
        CancellationToken ct = default);
}

public sealed record MemoryVectorMatch
{
    public required MemoryVectorRecord Record { get; init; }
    public required double Similarity { get; init; }
}


