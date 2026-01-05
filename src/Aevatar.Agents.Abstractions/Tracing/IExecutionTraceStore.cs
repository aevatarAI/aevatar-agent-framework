namespace Aevatar.Agents.Abstractions.Tracing;

// ============================================================
//  Execution Trace Store (Abstraction)
//
//  WHY:
//  - ExecutionTrace is the unified, exportable Protobuf trace contract.
//  - Store abstraction enables: file / DB / cloud storage implementations.
//
//  NOTE:
//  - This interface is NOT a cross-runtime message. It is a DI boundary.
//  - The stored payload (`ExecutionTrace`) IS a Protobuf message.
// ============================================================
public interface IExecutionTraceStore
{
    Task SaveAsync(ExecutionTrace trace, CancellationToken ct = default);

    Task<ExecutionTrace?> LoadAsync(string executionId, CancellationToken ct = default);

    Task<bool> ExistsAsync(string executionId, CancellationToken ct = default);

    Task DeleteAsync(string executionId, CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionTraceBundleInfo>> ListAsync(int limit = 200, CancellationToken ct = default);
}

/// <summary>
/// Lightweight bundle metadata for listing purposes.
/// </summary>
public sealed record ExecutionTraceBundleInfo
{
    public required string ExecutionId { get; init; }
    public ExecutionTraceKind Kind { get; init; }
    public ExecutionTraceStatus Status { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; init; }
}


