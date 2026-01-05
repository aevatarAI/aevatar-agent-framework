using Aevatar.Agents.Abstractions.Tracing;

namespace Aevatar.Agents.Core.Tracing;

// ============================================================
//  NullExecutionTraceStore
//
//  Default behavior: do nothing.
//  This avoids unexpected disk writes unless the user explicitly
//  enables tracing output (e.g., via AEVATAR_TRACE_DIR).
// ============================================================
public sealed class NullExecutionTraceStore : IExecutionTraceStore
{
    public static readonly NullExecutionTraceStore Instance = new();

    public Task SaveAsync(ExecutionTrace trace, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ExecutionTrace?> LoadAsync(string executionId, CancellationToken ct = default) =>
        Task.FromResult<ExecutionTrace?>(null);

    public Task<bool> ExistsAsync(string executionId, CancellationToken ct = default) => Task.FromResult(false);

    public Task DeleteAsync(string executionId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<ExecutionTraceBundleInfo>> ListAsync(int limit = 200, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExecutionTraceBundleInfo>>([]);
}


