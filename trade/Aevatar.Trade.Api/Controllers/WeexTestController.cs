using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api.Controllers;

/// <summary>
/// WEEX API testing endpoints (for hackathon pre-test).
///
/// Notes:
/// - Does NOT depend on LLM or TradingSystem initialization.
/// - Uses IWeexApiClient configured via appsettings + appsettings.secrets.json.
/// </summary>
[ApiController]
[Route("api/weex-test")]
public class WeexTestController : ControllerBase
{
    private readonly IWeexApiClient _weex;
    private readonly ILogger<WeexTestController> _logger;

    public WeexTestController(IWeexApiClient weex, ILogger<WeexTestController> logger)
    {
        _weex = weex;
        _logger = logger;
    }

    [HttpGet("ticker")]
    public async Task<IActionResult> GetTicker([FromQuery] string symbol = "cmt_btcusdt", CancellationToken ct = default)
    {
        try
        {
            var ticker = await _weex.GetTickerAsync(symbol, ct);
            return Ok(ticker);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX ticker failed: {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances(CancellationToken ct = default)
    {
        try
        {
            var balances = await _weex.GetBalancesAsync(ct);
            return Ok(new { count = balances.Count, balances });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX balances failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("positions")]
    public async Task<IActionResult> GetPositions([FromQuery] string? symbol = null, CancellationToken ct = default)
    {
        try
        {
            var positions = await _weex.GetPositionsAsync(symbol, ct);
            return Ok(new { count = positions.Count, positions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX positions failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("fills")]
    public async Task<IActionResult> GetFills(
        [FromQuery] string? symbol = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            var fills = await _weex.GetFillsAsync(symbol, limit, ct);
            return Ok(new { count = fills.Count, fills });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX fills failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("open-orders")]
    public async Task<IActionResult> GetOpenOrders([FromQuery] string? symbol = null, CancellationToken ct = default)
    {
        try
        {
            var orders = await _weex.GetOpenOrdersAsync(symbol, ct);
            return Ok(new { count = orders.Count, orders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX open orders failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("order")]
    public async Task<IActionResult> GetOrder(
        [FromQuery] string symbol,
        [FromQuery] string? orderId = null,
        [FromQuery] string? clientOrderId = null,
        CancellationToken ct = default)
    {
        try
        {
            var order = await _weex.GetOrderAsync(symbol, orderId, clientOrderId, ct);
            return Ok(new { found = order != null, order });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX get order failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("place-order")]
    public async Task<IActionResult> PlaceOrder([FromBody] OrderRequest request, CancellationToken ct = default)
    {
        try
        {
            var result = await _weex.PlaceOrderAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX place order failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("cancel-order")]
    public async Task<IActionResult> CancelOrder(
        [FromQuery] string symbol,
        [FromQuery] string? orderId = null,
        [FromQuery] string? clientOrderId = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _weex.CancelOrderAsync(symbol, orderId, clientOrderId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WEEX cancel order failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}


