using Aevatar.Trade.Infrastructure.Exchanges;
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
        IExchangeClient exchangeClient,
        IOptions<ExchangeConfig> exchange,
        IOptions<TradingConfig> trading,
        IOptions<DecisionTriggerConfig> trigger,
        IOptions<TradingPolicyConfig> policy,
        IOptions<TradeAuditConfig> audit,
        IOptions<AiWarsLogUploadConfig> aiWars,
        IOptions<Aevatar.Trade.Infrastructure.WeexApi.WeexApiConfig> weex)
    {
        var ex = exchange.Value;
        var t = trading.Value;
        var tr = trigger.Value;
        var p = policy.Value;
        var a = audit.Value;
        var w = weex.Value;
        var u = aiWars.Value;
        var caps = exchangeClient.Capabilities;

        return Ok(new
        {
            exchange = new
            {
                type = ex.ExchangeType.ToString(),
                mode = ex.Mode,
                symbols = ex.Symbols,
                enableWebsocket = ex.EnableWebsocket,
                enableRestPolling = ex.EnableRestPolling,
                capabilities = new
                {
                    supportsWebSocket = caps.SupportsWebSocket,
                    supportsPositions = caps.SupportsPositions,
                    supportsFundingRate = caps.SupportsFundingRate,
                    supportsOpenInterest = caps.SupportsOpenInterest,
                    supportsFills = caps.SupportsFills,
                    supportsBalances = caps.SupportsBalances,
                    supportsOrders = caps.SupportsOrders,
                    supportsKlines = caps.SupportsKlines
                }
            },
            trading = new
            {
                symbol = t.Symbol,
                interval = t.Interval,
                executionMode = t.ExecutionMode.ToString(),
                minConfidenceToTrade = t.MinConfidenceToTrade,
                maxPositionPct = t.MaxPositionPct,
                maxTotalPositionPct = t.MaxTotalPositionPct
            },
            trigger = new
            {
                priceChangePct = tr.PriceChangePct,
                priceChangeAbs = tr.PriceChangeAbs,
                windowSeconds = tr.WindowSeconds,
                cooldownSeconds = tr.CooldownSeconds,
                triggerOnStartup = tr.TriggerOnStartup
            },
            policy = new
            {
                trading = new
                {
                    symbol = p.Trading?.Symbol ?? "",
                    interval = p.Trading?.Interval ?? "",
                    executionMode = p.Trading?.ExecutionMode.ToString() ?? "",
                    minConfidenceToTrade = p.Trading?.MinConfidenceToTrade ?? 0,
                    maxPositionPct = p.Trading?.MaxPositionPct ?? 0,
                    maxTotalPositionPct = p.Trading?.MaxTotalPositionPct ?? 0
                },
                risk = new
                {
                    maxConsecutiveLosses = p.Risk?.MaxConsecutiveLosses ?? 0,
                    cooldownMinutes = p.Risk?.CooldownMinutes ?? 0,
                    stopLossPct = p.Risk?.StopLossPct ?? 0,
                    takeProfitPct = p.Risk?.TakeProfitPct ?? 0
                },
                analysis = new
                {
                    sentimentWeight = p.Analysis?.SentimentWeight ?? 0,
                    technicalWeight = p.Analysis?.TechnicalWeight ?? 0,
                    newsWeight = p.Analysis?.NewsWeight ?? 0
                },
                trigger = new
                {
                    priceChangePct = p.Trigger?.PriceChangePct ?? 0,
                    priceChangeAbs = p.Trigger?.PriceChangeAbs ?? 0,
                    windowSeconds = p.Trigger?.WindowSeconds ?? 0,
                    cooldownSeconds = p.Trigger?.CooldownSeconds ?? 0,
                    triggerOnStartup = p.Trigger?.TriggerOnStartup ?? false
                }
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


