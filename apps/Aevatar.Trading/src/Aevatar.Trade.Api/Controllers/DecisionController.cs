using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api.Controllers;

// ============================================================================
//  Decision API (trigger-driven)
// ============================================================================

[ApiController]
[Route("api/[controller]")]
public sealed class DecisionController : ControllerBase
{
    private readonly TradingSystem _tradingSystem;
    private readonly ILogger<DecisionController> _logger;

    public DecisionController(TradingSystem tradingSystem, ILogger<DecisionController> logger)
    {
        _tradingSystem = tradingSystem;
        _logger = logger;
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> Trigger([FromBody] TriggerDecisionRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "symbol is required" });

        try
        {
            var evt = await _tradingSystem.TriggerDecisionAsync(
                request.Symbol.Trim(),
                request.Reason,
                request.DeltaPct,
                request.DeltaAbs,
                ct);
            return Ok(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger decision");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed class TriggerDecisionRequest
{
    public string Symbol { get; set; } = "";
    public string? Reason { get; set; }
    public double? DeltaPct { get; set; }
    public double? DeltaAbs { get; set; }
}
