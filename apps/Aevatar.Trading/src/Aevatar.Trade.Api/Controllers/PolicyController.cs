using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api.Controllers;

// ============================================================================
//  Policy API (exchange-agnostic)
// ============================================================================

[ApiController]
[Route("api/[controller]")]
public sealed class PolicyController : ControllerBase
{
    private readonly TradingSystem _tradingSystem;
    private readonly ILogger<PolicyController> _logger;

    public PolicyController(TradingSystem tradingSystem, ILogger<PolicyController> logger)
    {
        _tradingSystem = tradingSystem;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
    {
        try
        {
            var policy = await _tradingSystem.GetPolicyAsync();
            return Ok(policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get policy");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePolicy([FromBody] UpdatePolicyRequest request, CancellationToken ct)
    {
        if (request?.Policy == null)
            return BadRequest(new { error = "policy is required" });

        try
        {
            var evt = await _tradingSystem.UpdatePolicyAsync(request.Policy, request.UpdatedBy, request.Reason, ct);
            return Ok(new { message = "policy updated", @event = evt });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update policy");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed class UpdatePolicyRequest
{
    public TradingPolicyConfig? Policy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? Reason { get; set; }
}
