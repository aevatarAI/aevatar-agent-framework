using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Api.Controllers;

/// <summary>
/// Frontend metadata endpoint (safe config snapshot, no secrets).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MetaController : ControllerBase
{
    [HttpGet]
    public IActionResult Get(
        IOptions<TradingConfig> trading,
        IOptions<TradeAuditConfig> audit,
        IOptions<AiWarsLogUploadConfig> aiWars,
        IOptions<Aevatar.Trade.Infrastructure.WeexApi.WeexApiConfig> weex)
    {
        var t = trading.Value;
        var a = audit.Value;
        var w = weex.Value;
        var u = aiWars.Value;

        return Ok(new
        {
            trading = new
            {
                symbol = t.Symbol,
                interval = t.Interval,
                executionMode = t.ExecutionMode.ToString(),
                minConfidenceToTrade = t.MinConfidenceToTrade,
                maxPositionPct = t.MaxPositionPct,
                maxTotalPositionPct = t.MaxTotalPositionPct
            },
            weex = new
            {
                mode = w.Mode.ToString(),
                baseUrl = w.BaseUrl,
                marketDataBaseUrl = w.MarketDataBaseUrl,
                tradingBaseUrl = w.TradingBaseUrl,
                publicWebSocketUrl = w.PublicWebSocketUrl,
                webSocketOrigin = w.WebSocketOrigin
            },
            audit = new
            {
                enabled = a.Enabled,
                outputDir = a.OutputDir,
                includeMarketData = a.IncludeMarketData,
                requestAiWarsUpload = a.RequestAiwarsUpload
            },
            aiWars = new
            {
                enabled = u.Enabled,
                baseUrl = u.BaseUrl,
                uploadPath = u.UploadPath
            }
        });
    }
}


