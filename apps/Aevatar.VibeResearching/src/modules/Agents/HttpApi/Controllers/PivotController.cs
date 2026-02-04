using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Application.Contracts.Services;

namespace Aevatar.VibeResearching.Agents.Controllers;

/// <summary>
/// Research direction pivot controller.
/// NOTE: Rollback and snapshot-listing routes are served by PivotEndpoints (MinimalAPI)
/// which provides complete business logic with direct domain service access.
/// This controller only exposes routes that have no MinimalAPI equivalent.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}/pivot")]
[Authorize]
public class PivotController : AbpControllerBase
{
    private readonly IPivotAppService _pivotAppService;

    public PivotController(IPivotAppService pivotAppService)
    {
        _pivotAppService = pivotAppService;
    }

    /// <summary>
    /// Gets the current pivot status.
    /// GET /api/sessions/{sessionId}/pivot/status
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatusAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var status = await _pivotAppService.GetStatusAsync(sessionId, ct);
        return Ok(status);
    }

    /// <summary>
    /// Confirms a pivot direction (creates a snapshot).
    /// POST /api/sessions/{sessionId}/pivot/confirm
    /// </summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmPivotAsync(
        [FromRoute] string sessionId,
        [FromBody] ConfirmPivotDto input,
        CancellationToken ct = default)
    {
        var snapshot = await _pivotAppService.ConfirmPivotAsync(
            sessionId,
            input.DirectionSummary ?? string.Empty,
            ct);

        return Ok(snapshot);
    }
}

/// <summary>
/// DTO for confirming pivot direction.
/// </summary>
public class ConfirmPivotDto
{
    public string? DirectionSummary { get; set; }
}
