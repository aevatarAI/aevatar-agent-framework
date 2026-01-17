using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api.Controllers;

// ============================================================================
//  Positions API (exchange-agnostic)
// ============================================================================

[ApiController]
[Route("api/[controller]")]
public sealed class PositionsController : ControllerBase
{
    private readonly TradingSystem _tradingSystem;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(TradingSystem tradingSystem, ILogger<PositionsController> logger)
    {
        _tradingSystem = tradingSystem;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? symbol, CancellationToken ct)
    {
        if (!_tradingSystem.SupportsPositions)
            return Ok(new { supported = false, count = 0, positions = Array.Empty<object>() });

        try
        {
            var positions = await _tradingSystem.GetPositionsAsync(symbol, ct);
            return Ok(new { supported = true, count = positions.Count, positions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get positions");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
