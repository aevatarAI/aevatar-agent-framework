using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Pivot;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Graph pivot controller for pivot snapshot operations via /graph route.
/// Maps frontend /graph/pivot and /graph/pivots routes to the pivot snapshot manager.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}/graph")]
[Authorize]
public class GraphPivotController : AbpControllerBase
{
    private readonly IPivotSnapshotManager _pivotSnapshotManager;

    public GraphPivotController(IPivotSnapshotManager pivotSnapshotManager)
    {
        _pivotSnapshotManager = pivotSnapshotManager;
    }

    /// <summary>
    /// Creates a pivot snapshot (alternative route for frontend compatibility).
    /// POST /api/sessions/{sessionId}/graph/pivot
    /// </summary>
    [HttpPost("pivot")]
    public async Task<IActionResult> CreatePivotAsync(
        [FromRoute] string sessionId,
        [FromBody] CreatePivotDto? input,
        CancellationToken ct = default)
    {
        var directionSummary = input?.DirectionSummary ?? "Pivot snapshot created";
        var snapshot = await _pivotSnapshotManager.CreateSnapshotAsync(
            sessionId,
            directionSummary,
            ct);

        return Ok(new
        {
            ok = true,
            sessionId,
            pivotId = snapshot.PivotId,
            snapshotId = snapshot.SnapshotId,
            directionSummary = snapshot.DirectionSummary,
            createdAt = snapshot.CreatedAt,
            expiresAt = snapshot.ExpiresAt
        });
    }

    /// <summary>
    /// Lists all pivot snapshots for a session (alternative route for frontend compatibility).
    /// GET /api/sessions/{sessionId}/graph/pivots
    /// </summary>
    [HttpGet("pivots")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPivotsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var result = await _pivotSnapshotManager.ListSnapshotsAsync(sessionId, ct);
        return Ok(result);
    }
}

/// <summary>
/// DTO for creating a pivot snapshot.
/// </summary>
public class CreatePivotDto
{
    public string? DirectionSummary { get; set; }
}
