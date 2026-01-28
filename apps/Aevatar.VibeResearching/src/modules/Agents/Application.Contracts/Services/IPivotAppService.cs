using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Contracts.Services;

/// <summary>
/// Application service for research direction pivot and rollback operations.
/// </summary>
public interface IPivotAppService : IApplicationService
{
    /// <summary>
    /// Rolls back to a specific pivot snapshot.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="pivotId">Pivot snapshot identifier</param>
    /// <param name="preserveNewCompleted">Whether to preserve newly completed nodes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Rollback result with restored node counts</returns>
    Task<object> RollbackAsync(string sessionId, string pivotId, bool preserveNewCompleted = false, CancellationToken ct = default);

    /// <summary>
    /// Rolls back to the most recent pivot snapshot.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="preserveNewCompleted">Whether to preserve newly completed nodes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Rollback result with restored node counts</returns>
    Task<object> RollbackToMostRecentAsync(string sessionId, bool preserveNewCompleted = false, CancellationToken ct = default);

    /// <summary>
    /// Lists available rollback snapshots for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of available snapshots with metadata</returns>
    Task<object> GetSnapshotsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current pivot status for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Pivot status information</returns>
    Task<object> GetStatusAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Confirms a pivot direction (creates a snapshot).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="directionSummary">Summary of the new direction</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created pivot snapshot</returns>
    Task<object> ConfirmPivotAsync(string sessionId, string directionSummary, CancellationToken ct = default);
}
