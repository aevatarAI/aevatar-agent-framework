using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure.Controllers;

/// <summary>
/// Research deliverables management controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
[Authorize]
public class DeliveryController : AbpControllerBase
{
    private readonly IDeliveryAppService _deliveryAppService;

    public DeliveryController(IDeliveryAppService deliveryAppService)
    {
        _deliveryAppService = deliveryAppService;
    }

    /// <summary>
    /// Gets research brief and delivery snapshot.
    /// GET /api/sessions/{sessionId}/deliverables
    /// </summary>
    [HttpGet("deliverables")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDeliverablesAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var brief = await _deliveryAppService.GetBriefAsync(sessionId, ct);
        var delivery = await _deliveryAppService.GetDeliveryAsync(sessionId, ct);

        return Ok(new
        {
            ok = true,
            sessionId,
            brief,
            delivery
        });
    }

    /// <summary>
    /// Gets research brief snapshot.
    /// GET /api/sessions/{sessionId}/brief
    /// </summary>
    [HttpGet("brief")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBriefAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var brief = await _deliveryAppService.GetBriefAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, brief });
    }

    /// <summary>
    /// Gets delivery center snapshot.
    /// GET /api/sessions/{sessionId}/delivery
    /// </summary>
    [HttpGet("delivery")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDeliveryAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var delivery = await _deliveryAppService.GetDeliveryAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, delivery });
    }

    /// <summary>
    /// Gets goals snapshot.
    /// GET /api/sessions/{sessionId}/goals
    /// </summary>
    [HttpGet("goals")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGoalsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var goals = await _deliveryAppService.GetGoalsAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, goals });
    }

    /// <summary>
    /// Updates goals for a session.
    /// PUT /api/sessions/{sessionId}/goals
    /// </summary>
    [HttpPut("goals")]
    public async Task<IActionResult> UpdateGoalsAsync(
        [FromRoute] string sessionId,
        [FromBody] SraGoalsSnapshot goals,
        CancellationToken ct = default)
    {
        await _deliveryAppService.UpdateGoalsAsync(sessionId, goals, ct);
        return Ok(new { ok = true, sessionId });
    }

    /// <summary>
    /// Gets conclusion cards snapshot.
    /// GET /api/sessions/{sessionId}/conclusion-cards
    /// </summary>
    [HttpGet("conclusion-cards")]
    [AllowAnonymous]
    public async Task<IActionResult> GetConclusionCardsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var cards = await _deliveryAppService.GetConclusionCardsAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, cards });
    }

    /// <summary>
    /// Gets evidence table snapshot.
    /// GET /api/sessions/{sessionId}/evidence-table
    /// </summary>
    [HttpGet("evidence-table")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEvidenceTableAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var evidence = await _deliveryAppService.GetEvidenceTableAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, evidence });
    }

    /// <summary>
    /// Gets next tasks snapshot.
    /// GET /api/sessions/{sessionId}/next-tasks
    /// </summary>
    [HttpGet("next-tasks")]
    [AllowAnonymous]
    public async Task<IActionResult> GetNextTasksAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var tasks = await _deliveryAppService.GetNextTasksAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, tasks });
    }
}
