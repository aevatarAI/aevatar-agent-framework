namespace Aevatar.Agents.Abstractions.Memory;

// ============================================================
//  Memory Graph Store (Abstraction)
//
//  WHY:
//  - MemoryGraph is a portable Protobuf artifact derived from traces/events.
//  - Store abstraction enables: file / DB / graph DB implementations.
//
//  NOTE:
//  - This interface is a DI boundary (NOT a cross-runtime message).
// ============================================================
public interface IMemoryGraphStore
{
    Task SaveAsync(MemoryGraph graph, CancellationToken ct = default);

    Task<MemoryGraph?> LoadAsync(string graphId, CancellationToken ct = default);

    Task<bool> ExistsAsync(string graphId, CancellationToken ct = default);

    Task DeleteAsync(string graphId, CancellationToken ct = default);
}


