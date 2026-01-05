using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.MemoryGraphs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.Tracing;

// ============================================================
//  ProjectingExecutionTraceStore
//
//  PURPOSE:
//  - Decorate an IExecutionTraceStore so that when a trace is saved,
//    we best-effort project it into:
//    - MemoryGraph (trace artifact)
//    - MemoryEntry (execution-scoped, searchable text)
//
//  PRINCIPLES:
//  - Best-effort: projection failures never break trace persistence.
//  - No external dependencies: graph store defaults to file artifacts.
// ============================================================
public sealed class ProjectingExecutionTraceStore : IExecutionTraceStore
{
    private readonly IExecutionTraceStore _inner;
    private readonly IMemoryStore _memoryStore;
    private readonly IMemoryGraphStore _graphStore;
    private readonly ExecutionTraceMemoryProjector _projector;
    private readonly ILogger _logger;

    public ProjectingExecutionTraceStore(
        IExecutionTraceStore inner,
        IMemoryStore memoryStore,
        IMemoryGraphStore graphStore,
        ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _projector = new ExecutionTraceMemoryProjector(logger);
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task SaveAsync(ExecutionTrace trace, CancellationToken ct = default)
    {
        await _inner.SaveAsync(trace, ct);

        try
        {
            await _projector.ProjectAsync(trace, _memoryStore, _graphStore, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Trace projection failed (best-effort)");
        }
    }

    public Task<ExecutionTrace?> LoadAsync(string executionId, CancellationToken ct = default)
        => _inner.LoadAsync(executionId, ct);

    public Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
        => _inner.ExistsAsync(executionId, ct);

    public async Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(executionId, ct);

        // best-effort delete graph artifact; memory entries are append-only for now.
        try
        {
            await _graphStore.DeleteAsync(executionId, ct);
        }
        catch
        {
            // ignore
        }
    }

    public Task<IReadOnlyList<ExecutionTraceBundleInfo>> ListAsync(int limit = 200, CancellationToken ct = default)
        => _inner.ListAsync(limit, ct);
}


