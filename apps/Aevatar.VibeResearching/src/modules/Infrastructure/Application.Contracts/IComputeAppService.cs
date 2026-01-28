using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for compute requests and execution management.
/// </summary>
public interface IComputeAppService : IApplicationService
{
    /// <summary>
    /// Records a user decision on a compute plan.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="input">Decision data</param>
    /// <param name="ct">Cancellation token</param>
    Task RecordDecisionAsync(string sessionId, ComputeDecisionDto input, CancellationToken ct = default);

    /// <summary>
    /// Gets a compute plan by ID.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="planId">Plan identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Compute plan</returns>
    Task<SraComputePlan> GetPlanAsync(string sessionId, string planId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a compute job.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="jobId">Job identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Job status</returns>
    Task<SraComputeJobStatus> GetJobStatusAsync(string sessionId, string jobId, CancellationToken ct = default);

    /// <summary>
    /// Lists all compute requests for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of compute requests</returns>
    Task<IReadOnlyList<SraComputeRequest>> GetRequestsAsync(string sessionId, CancellationToken ct = default);
}
