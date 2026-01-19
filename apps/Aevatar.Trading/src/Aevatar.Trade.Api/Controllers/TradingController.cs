using Microsoft.AspNetCore.Mvc;

namespace Aevatar.Trade.Api.Controllers;

/// <summary>
/// Trading system controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TradingController : ControllerBase
{
    private readonly TradingSystem _tradingSystem;
    private readonly ILogger<TradingController> _logger;

    public TradingController(
        TradingSystem tradingSystem,
        ILogger<TradingController> logger)
    {
        _tradingSystem = tradingSystem;
        _logger = logger;
    }

    /// <summary>
    /// Initialize trading system
    /// </summary>
    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize(CancellationToken ct)
    {
        try
        {
            await _tradingSystem.InitializeAsync(ct);
            return Ok(new { message = "Trading system initialized successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize trading system");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Start trading
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        try
        {
            await _tradingSystem.StartAsync(ct);
            return Ok(new { message = "Trading system started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start trading system");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Stop trading
    /// </summary>
    [HttpPost("stop")]
    public async Task<IActionResult> Stop([FromQuery] string reason = "API request")
    {
        try
        {
            await _tradingSystem.StopAsync(reason);
            return Ok(new { message = "Trading system stopped", reason });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop trading system");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get system status
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var status = await _tradingSystem.GetStatusAsync();
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trading system status");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Sync account information
    /// </summary>
    [HttpPost("sync-account")]
    public async Task<IActionResult> SyncAccount()
    {
        try
        {
            await _tradingSystem.SyncAccountInfoAsync();
            return Ok(new { message = "Account info synced" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync account info");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// Agent status controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly TradingSystem _tradingSystem;

    public AgentsController(TradingSystem tradingSystem)
    {
        _tradingSystem = tradingSystem;
    }

    /// <summary>
    /// Get all agent statuses
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllAgents()
    {
        var status = await _tradingSystem.GetStatusAsync();
        return Ok(new
        {
            agents = new[]
            {
                new { name = "DataCollector", status = status.DataCollector },
                new { name = "SentimentAnalyst", status = status.SentimentAnalyst },
                new { name = "TechnicalAnalyst", status = status.TechnicalAnalyst },
                new { name = "Coordinator", status = status.Coordinator },
                new { name = "DecisionTrigger", status = status.DecisionTrigger },
                new { name = "PolicyManager", status = status.PolicyManager },
                new { name = "RiskManager", status = status.RiskManager },
                new { name = "Executor", status = status.Executor }
            }
        });
    }
}
