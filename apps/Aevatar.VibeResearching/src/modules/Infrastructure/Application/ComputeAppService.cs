using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for compute requests and execution management.
/// Delegates to compute decision repository.
/// </summary>
public class ComputeAppService : ApplicationService, IComputeAppService
{
    private readonly IComputeDecisionRepository _computeRepository;

    public ComputeAppService(IComputeDecisionRepository computeRepository)
    {
        _computeRepository = computeRepository;
    }

    /// <inheritdoc/>
    public async Task RecordDecisionAsync(string sessionId, ComputeDecisionDto input, CancellationToken ct = default)
    {
        await _computeRepository.RecordDecisionAsync(
            sessionId,
            input.PlanId ?? string.Empty,
            input.Decision ?? string.Empty,
            ct);
    }

    /// <inheritdoc/>
    public async Task<SraComputePlan> GetPlanAsync(string sessionId, string planId, CancellationToken ct = default)
    {
        var result = await _computeRepository.GetPlanAsync(sessionId, planId, ct);
        return (SraComputePlan)result;
    }

    /// <inheritdoc/>
    public async Task<SraComputeJobStatus> GetJobStatusAsync(string sessionId, string jobId, CancellationToken ct = default)
    {
        var result = await _computeRepository.GetJobStatusAsync(sessionId, jobId, ct);
        return (SraComputeJobStatus)result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SraComputeRequest>> GetRequestsAsync(string sessionId, CancellationToken ct = default)
    {
        var result = await _computeRepository.GetRequestsAsync(sessionId, ct);
        return result.Cast<SraComputeRequest>().ToList();
    }
}
