using Aevatar.VibeResearching.Agents.Application.Contracts.Services;
using Aevatar.VibeResearching.Agents.Pivot;
using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Services;

/// <summary>
/// Application service for research direction pivot and rollback operations.
/// Delegates to pivot orchestrator and snapshot manager.
/// </summary>
public class PivotAppService : ApplicationService, IPivotAppService
{
    private readonly IPivotOrchestrator _pivotOrchestrator;
    private readonly IPivotSnapshotManager _snapshotManager;
    private readonly IPivotStatusService _statusService;

    public PivotAppService(
        IPivotOrchestrator pivotOrchestrator,
        IPivotSnapshotManager snapshotManager,
        IPivotStatusService statusService)
    {
        _pivotOrchestrator = pivotOrchestrator;
        _snapshotManager = snapshotManager;
        _statusService = statusService;
    }

    /// <inheritdoc/>
    public async Task<object> RollbackAsync(
        string sessionId,
        string pivotId,
        bool preserveNewCompleted = false,
        CancellationToken ct = default)
    {
        return await _pivotOrchestrator.RollbackToPivotAsync(sessionId, pivotId, preserveNewCompleted, ct);
    }

    /// <inheritdoc/>
    public async Task<object> RollbackToMostRecentAsync(
        string sessionId,
        bool preserveNewCompleted = false,
        CancellationToken ct = default)
    {
        return await _pivotOrchestrator.RollbackToMostRecentAsync(sessionId, preserveNewCompleted, ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetSnapshotsAsync(string sessionId, CancellationToken ct = default)
    {
        return await _snapshotManager.ListSnapshotsAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        return await _statusService.GetStatusAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<object> ConfirmPivotAsync(
        string sessionId,
        string directionSummary,
        CancellationToken ct = default)
    {
        return await _snapshotManager.CreateSnapshotAsync(sessionId, directionSummary, ct);
    }
}
