using Microsoft.Extensions.Logging;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for compute execution decisions.
/// Persists user decisions for compute operations (execute/degrade/skip).
/// </summary>
public interface IComputeDecisionRepository
{
    /// <summary>
    /// Saves a compute decision and returns the relative path to the saved file.
    /// </summary>
    Task<string> SaveAsync(
        string sessionId,
        string planId,
        string action,
        string? comment,
        CancellationToken ct);

    /// <summary>
    /// Writes a compute decision (alias for SaveAsync).
    /// </summary>
    Task<string> WriteDecisionAsync(
        string sessionId,
        string planId,
        string action,
        string? comment,
        CancellationToken ct);

    /// <summary>
    /// Records a compute decision (alias for SaveAsync).
    /// </summary>
    Task RecordDecisionAsync(
        string sessionId,
        string planId,
        string decision,
        CancellationToken ct);

    /// <summary>
    /// Gets a compute plan.
    /// </summary>
    Task<object> GetPlanAsync(
        string sessionId,
        string planId,
        CancellationToken ct);

    /// <summary>
    /// Gets compute job status.
    /// </summary>
    Task<object> GetJobStatusAsync(
        string sessionId,
        string jobId,
        CancellationToken ct);

    /// <summary>
    /// Gets all compute requests for a session.
    /// </summary>
    Task<IReadOnlyList<object>> GetRequestsAsync(
        string sessionId,
        CancellationToken ct);
}
