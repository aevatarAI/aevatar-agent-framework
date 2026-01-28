using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure.Controllers;

/// <summary>
/// Compute requests and execution management controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}/compute")]
public class ComputeController : AbpControllerBase
{
    private readonly IComputeAppService _computeAppService;

    public ComputeController(IComputeAppService computeAppService)
    {
        _computeAppService = computeAppService;
    }

    /// <summary>
    /// Records a user decision on a compute plan.
    /// POST /api/sessions/{sessionId}/compute/decision
    /// </summary>
    [HttpPost("decision")]
    public async Task<IActionResult> RecordDecisionAsync(
        [FromRoute] string sessionId,
        [FromBody] ComputeDecisionDto input,
        CancellationToken ct = default)
    {
        await _computeAppService.RecordDecisionAsync(sessionId, input, ct);
        return Ok(new { ok = true, sessionId, planId = input.PlanId });
    }

    /// <summary>
    /// Gets a compute plan by ID.
    /// GET /api/sessions/{sessionId}/compute/plans/{planId}
    /// </summary>
    [HttpGet("plans/{planId}")]
    public async Task<IActionResult> GetPlanAsync(
        [FromRoute] string sessionId,
        [FromRoute] string planId,
        CancellationToken ct = default)
    {
        var plan = await _computeAppService.GetPlanAsync(sessionId, planId, ct);
        return Ok(new { ok = true, sessionId, plan });
    }

    /// <summary>
    /// Gets the status of a compute job.
    /// GET /api/sessions/{sessionId}/compute/jobs/{jobId}/status
    /// </summary>
    [HttpGet("jobs/{jobId}/status")]
    public async Task<IActionResult> GetJobStatusAsync(
        [FromRoute] string sessionId,
        [FromRoute] string jobId,
        CancellationToken ct = default)
    {
        var status = await _computeAppService.GetJobStatusAsync(sessionId, jobId, ct);
        return Ok(new { ok = true, sessionId, status });
    }

    /// <summary>
    /// Lists all compute requests for a session.
    /// GET /api/sessions/{sessionId}/compute/requests
    /// </summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequestsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var requests = await _computeAppService.GetRequestsAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, requests });
    }
}
