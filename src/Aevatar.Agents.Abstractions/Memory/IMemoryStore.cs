using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Abstractions.Memory;

// ============================================================
//  Memory Store (Abstraction)
//
//  WHY:
//  - MemoryEntry is a cross-runtime Protobuf contract.
//  - Store abstraction enables: in-memory / file / DB implementations.
//
//  NOTE:
//  - This interface is a DI boundary (NOT a cross-runtime message).
// ============================================================
public interface IMemoryStore
{
    Task AppendAsync(MemoryEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default);

    Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default);

    Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default);
}