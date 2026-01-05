using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Trade.Agents.Analysts;
using Aevatar.Trade.Agents.Audit;
using Aevatar.Trade.Agents.AiWars;
using Aevatar.Trade.Agents.Coordinator;
using Aevatar.Trade.Agents.Data;
using Aevatar.Trade.Agents.Execution;
using Aevatar.Trade.Agents.RiskControl;
using Aevatar.Trade.Infrastructure.AiWars;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Aevatar.Trade;

/// <summary>
/// Trading system entry point
/// Responsible for creating and orchestrating all agents
/// </summary>
public class TradingSystem : IAsyncDisposable
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IWeexApiClient _apiClient;
    private readonly IWeexAiWarsLogClient _aiWarsClient;
    private readonly WeexWebSocketClient _wsClient;
    private readonly TradingConfig _tradingConfig;
    private readonly AnalysisWeightConfig _analysisConfig;
    private readonly RiskControlConfig _riskConfig;
    private readonly TradeAuditConfig _auditConfig;
    private readonly AiWarsLogUploadConfig _aiWarsConfig;
    private readonly DecisionEngineConfig _decisionEngineConfig;
    private readonly CognitiveMeshDecisionEngine _cognitiveMeshDecisionEngine;
    private readonly LLMProvidersConfig _llmProvidersConfig;
    private readonly ILogger<TradingSystem> _logger;

    // Agent Actors
    private IGAgentActor? _dataCollectorActor;
    private IGAgentActor? _sentimentActor;
    private IGAgentActor? _technicalActor;
    private IGAgentActor? _coordinatorActor;
    private IGAgentActor? _riskManagerActor;
    private IGAgentActor? _executorActor;
    private IGAgentActor? _auditActor;
    private IGAgentActor? _aiWarsUploaderActor;

    public TradingSystem(
        IGAgentActorFactory actorFactory,
        IWeexApiClient apiClient,
        IWeexAiWarsLogClient aiWarsClient,
        WeexWebSocketClient wsClient,
        IOptions<TradingConfig> tradingConfig,
        IOptions<AnalysisWeightConfig> analysisConfig,
        IOptions<RiskControlConfig> riskConfig,
        IOptions<TradeAuditConfig> auditConfig,
        IOptions<AiWarsLogUploadConfig> aiWarsConfig,
        IOptions<DecisionEngineConfig> decisionEngineConfig,
        CognitiveMeshDecisionEngine cognitiveMeshDecisionEngine,
        IOptions<LLMProvidersConfig> llmProvidersConfig,
        ILogger<TradingSystem> logger)
    {
        _actorFactory = actorFactory;
        _apiClient = apiClient;
        _aiWarsClient = aiWarsClient;
        _wsClient = wsClient;
        _tradingConfig = tradingConfig.Value;
        _analysisConfig = analysisConfig.Value;
        _riskConfig = riskConfig.Value;
        _auditConfig = auditConfig.Value;
        _aiWarsConfig = aiWarsConfig.Value;
        _decisionEngineConfig = decisionEngineConfig.Value;
        _cognitiveMeshDecisionEngine = cognitiveMeshDecisionEngine;
        _llmProvidersConfig = llmProvidersConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the trading system
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Trading System...");

        var providerName = string.IsNullOrWhiteSpace(_llmProvidersConfig.Default)
            ? "openai-gpt4"
            : _llmProvidersConfig.Default;

        // ============ Create Agent Actors ============

        // 1. Data collector
        _dataCollectorActor = await _actorFactory.CreateGAgentActorAsync<DataCollectorAgent>(Guid.NewGuid().ToString(), ct);
        var dataCollector = (DataCollectorAgent)_dataCollectorActor.GetAgent();
        dataCollector.ApiClient = _apiClient;
        dataCollector.WebSocketClient = _wsClient;

        // 2. Analysts
        _sentimentActor = await _actorFactory.CreateGAgentActorAsync<MarketSentimentAgent>(Guid.NewGuid().ToString(), ct);
        var sentiment = (MarketSentimentAgent)_sentimentActor.GetAgent();
        sentiment.AllowDangerousTools = false; // safe default
        sentiment.ApiClient = _apiClient;     // for funding/openInterest (avoid placeholder metrics)
        await sentiment.InitializeAsync(providerName, cancellationToken: ct);
        
        _technicalActor = await _actorFactory.CreateGAgentActorAsync<TechnicalAnalystAgent>(Guid.NewGuid().ToString(), ct);
        var technical = (TechnicalAnalystAgent)_technicalActor.GetAgent();
        technical.AllowDangerousTools = false; // safe default
        await technical.InitializeAsync(providerName, cancellationToken: ct);

        // 3. Coordinator
        _coordinatorActor = await _actorFactory.CreateGAgentActorAsync<TradingCoordinatorAgent>(Guid.NewGuid().ToString(), ct);
        var coordinator = (TradingCoordinatorAgent)_coordinatorActor.GetAgent();
        coordinator.AllowDangerousTools = false; // coordinator should not directly trade
        await coordinator.InitializeAsync(providerName, cancellationToken: ct);
        coordinator.Configure(
            _tradingConfig.MinConfidenceToTrade,
            _analysisConfig.SentimentWeight,
            _analysisConfig.TechnicalWeight,
            _analysisConfig.NewsWeight,
            _tradingConfig.ExecutionMode,
            _decisionEngineConfig.TimeoutSeconds);
        
        if (string.Equals(_decisionEngineConfig.Mode, "CognitiveMesh", StringComparison.OrdinalIgnoreCase))
        {
            coordinator.DecisionEngine = _cognitiveMeshDecisionEngine;
            _logger.LogInformation("Coordinator decision engine: CognitiveMesh");
        }
        else
        {
            coordinator.DecisionEngine = null; // Fallback to direct LLM
            _logger.LogInformation("Coordinator decision engine: Direct");
        }

        // 4. Risk manager
        _riskManagerActor = await _actorFactory.CreateGAgentActorAsync<RiskManagerAgent>(Guid.NewGuid().ToString(), ct);
        var riskManager = (RiskManagerAgent)_riskManagerActor.GetAgent();
        // Allow dangerous tools (order placement/cancel) only in Live mode.
        riskManager.AllowDangerousTools = _tradingConfig.ExecutionMode == TradeExecutionMode.Live;
        await riskManager.InitializeAsync(providerName, cancellationToken: ct);
        riskManager.Configure(
            _tradingConfig.MaxPositionPct,
            _tradingConfig.MaxTotalPositionPct,
            _tradingConfig.MaxLossPerTrade,
            _tradingConfig.MaxDailyLoss,
            _riskConfig.MaxConsecutiveLosses,
            _riskConfig.CooldownMinutes,
            _tradingConfig.MinConfidenceToTrade);

        // 5. Executor
        _executorActor = await _actorFactory.CreateGAgentActorAsync<ExecutorAgent>(Guid.NewGuid().ToString(), ct);
        var executor = (ExecutorAgent)_executorActor.GetAgent();
        executor.Configure(_tradingConfig.ExecutionMode);
        executor.ApiClient = _apiClient;
        
        // 6. Trade audit (optional)
        if (_auditConfig.Enabled)
        {
            _auditActor = await _actorFactory.CreateGAgentActorAsync<TradeAuditAgent>(Guid.NewGuid().ToString(), ct);
            var audit = (TradeAuditAgent)_auditActor.GetAgent();
            // Provide the effective LLM model name for AI Wars log payload (best-effort).
            var modelName = _llmProvidersConfig.Providers.TryGetValue(providerName, out var llm)
                ? llm.Model
                : providerName;
            audit.Configure(_auditConfig, aiModel: modelName);
        }
        
        // 7. AI Wars uploader (optional)
        if (_auditActor != null && (_aiWarsConfig.Enabled || _auditConfig.RequestAiwarsUpload))
        {
            _aiWarsUploaderActor = await _actorFactory.CreateGAgentActorAsync<AiWarsLogUploaderAgent>(Guid.NewGuid().ToString(), ct);
            // NOTE:
            // - Uploader executes the dotnet-file skill (weex_ai_order_upload_ai_log.cs) directly.
            // - It reads WEEX_* from env (set by Trade.Api Program.cs).
        }

        // ============ Establish Hierarchy ============
        // 
        // DataCollector (Data Source)
        //      │
        //      ├── SentimentAgent (Analyst)
        //      ├── TechnicalAgent (Analyst)
        //      │
        //      └── Coordinator (Decision Maker)
        //              │
        //              └── RiskManager (Risk Control)
        //                      │
        //                      └── Executor (Execution)

        // ============================================================
        //  Hierarchy design (IMPORTANT)
        //
        //  EventRouter semantics:
        //  - Up: only to parent (NO sibling fan-out)
        //  - Down: to all children
        //
        //  Therefore, analysts must be children of Coordinator so their analysis (Publish Up)
        //  can reach the Coordinator. DataCollector only needs to publish market data Down.
        //
        //  Topology:
        //    DataCollector
        //        ↓
        //    Coordinator
        //     ├── SentimentAgent
        //     ├── TechnicalAgent
        //     └── RiskManager
        //          └── Executor
        //               └── TradeAudit
        //                    └── AiWarsUploader
        // ============================================================

        await ActorHierarchyCoordinator.LinkAsync(_dataCollectorActor, _coordinatorActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _sentimentActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _technicalActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _riskManagerActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_riskManagerActor, _executorActor, _logger, ct);
        
        // Attach audit agent for event capture.
        //
        // NOTE:
        // - Each actor can only have ONE parent (EventRouter has a single ParentId).
        // - To capture Coordinator/Risk/Executor events consistently, link Audit as a child of Executor:
        //   Coordinator Down -> RiskManager -> Executor -> Audit
        //   RiskManager Down -> Executor -> Audit
        //   Executor Down -> Audit
        if (_auditActor != null)
        {
            await ActorHierarchyCoordinator.LinkAsync(_executorActor, _auditActor, _logger, ct);
        }
        
        // Audit -> Uploader (audit publishes AiWarsLogUploadRequestedEvent downward)
        if (_auditActor != null && _aiWarsUploaderActor != null)
        {
            await ActorHierarchyCoordinator.LinkAsync(_auditActor, _aiWarsUploaderActor, _logger, ct);
        }

        _logger.LogInformation("Trading System initialized with Agent hierarchy");
    }

    /// <summary>
    /// Start the trading system
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Trading System for {Symbol}...", _tradingConfig.Symbol);

        // Startup guard: ensure we have enough BTC value (>=10U by default) before starting the loop.
        // In Live mode this may place a small market order to top up; in DryRun it only logs.
        await EnsureMinBaseAssetValueOnStartAsync(ct);

        // Sync account information (after possible bootstrap buy)
        await SyncAccountInfoAsync();

        // Start data collection
        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        await dataCollector.StartCollectingAsync(
            new[] { _tradingConfig.Symbol },
            _tradingConfig.Interval,
            ct);

        // Fetch historical kline data (for technical analysis initialization)
        await dataCollector.FetchHistoricalKlinesAsync(
            _tradingConfig.Symbol,
            _tradingConfig.Interval,
            200,
            ct);

        _logger.LogInformation("Trading System started");
    }

    // ============================================================================
    //  Startup Guard: Ensure base asset value >= N USDT
    //  - Example: ensure BTC * lastPrice >= 10
    //  - Purpose: guarantee the system can always execute SELL/hedge operations in spot-like semantics
    //             and satisfy hackathon demo constraints.
    // ============================================================================

    private async Task EnsureMinBaseAssetValueOnStartAsync(CancellationToken ct)
    {
        var minUsd = _tradingConfig.MinBaseAssetUsdOnStart;
        if (minUsd <= 0)
            return;

        if (!TryParseBaseQuote(_tradingConfig.Symbol, out var baseAsset, out var quoteAsset))
        {
            _logger.LogWarning(
                "Startup guard skipped: cannot parse base/quote from symbol={Symbol}",
                _tradingConfig.Symbol);
            return;
        }

        if (!string.Equals(quoteAsset, "USDT", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Startup guard skipped: quoteAsset={Quote} is not USDT (symbol={Symbol})",
                quoteAsset, _tradingConfig.Symbol);
            return;
        }

        // Always read current price from WEEX to avoid stale assumptions.
        var ticker = await _apiClient.GetTickerAsync(_tradingConfig.Symbol, ct);
        var last = (double)ticker.LastPrice;
        if (last <= 0)
            throw new InvalidOperationException($"Startup guard failed: invalid lastPrice={ticker.LastPrice} for {_tradingConfig.Symbol}");

        var balances = await _apiClient.GetBalancesAsync(ct);
        var baseBalance = balances.FirstOrDefault(b => b.Currency.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))?.Balance ?? 0m;

        var baseValueUsd = (double)baseBalance * last;
        if (baseValueUsd >= minUsd)
        {
            _logger.LogInformation(
                "Startup guard OK: {Asset}={Balance} (≈{Value:F2} USDT) >= {Min:F2} USDT",
                baseAsset, baseBalance, baseValueUsd, minUsd);
            return;
        }

        var missingUsd = minUsd - baseValueUsd;
        var needQty = missingUsd / last;

        // Round up (avoid being just below due to rounding/price move).
        needQty = Math.Ceiling(needQty * 100_000_000d) / 100_000_000d;
        if (needQty <= 0)
            return;

        if (_tradingConfig.ExecutionMode != TradeExecutionMode.Live)
        {
            _logger.LogWarning(
                "Startup guard would buy {Qty} {Asset} (≈{Usd:F2} USDT) to reach >= {Min:F2} USDT, but ExecutionMode={Mode} so skip.",
                needQty, baseAsset, missingUsd, minUsd, _tradingConfig.ExecutionMode);
            return;
        }

        _logger.LogInformation(
            "Startup guard: {Asset} value is {Value:F2} USDT < {Min:F2}. Placing MARKET BUY to top up ≈{Usd:F2} USDT (qty={Qty}).",
            baseAsset, baseValueUsd, minUsd, missingUsd, needQty);

        var clientOrderId = $"BOOTSTRAP_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        var result = await _apiClient.PlaceOrderAsync(new OrderRequest
        {
            Symbol = _tradingConfig.Symbol,
            Side = "buy",
            OrderType = "market",
            Quantity = needQty.ToString("F8", CultureInfo.InvariantCulture),
            ClientOrderId = clientOrderId
        }, ct);

        if (!result.Success)
        {
            // Emit an audit event so "invisible" bootstrap actions are observable.
            if (_auditActor != null)
            {
                try
                {
                    await _auditActor.PublishEventAsync(new OrderFailedEvent
                    {
                        ClientOrderId = clientOrderId,
                        DecisionId = "BOOTSTRAP_GUARD",
                        Symbol = _tradingConfig.Symbol,
                        Side = "buy",
                        ErrorCode = result.ErrorCode ?? "UNKNOWN",
                        ErrorMessage = result.ErrorMessage ?? "Unknown error",
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                    }, Aevatar.Agents.EventDirection.Down, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to publish startup guard OrderFailedEvent to audit");
                }
            }

            throw new InvalidOperationException(
                $"Startup guard failed: unable to top up {baseAsset} to >= {minUsd} USDT. " +
                $"Error={result.ErrorCode} {result.ErrorMessage}");
        }

        _logger.LogInformation(
            "Startup guard BUY submitted: orderId={OrderId}, clientOrderId={ClientOrderId}",
            result.OrderId, result.ClientOrderId);

        // Emit an audit event so "invisible" bootstrap actions are observable in trade-audit.
        if (_auditActor != null)
        {
            try
            {
                await _auditActor.PublishEventAsync(new OrderExecutedEvent
                {
                    OrderId = result.OrderId ?? "",
                    ClientOrderId = result.ClientOrderId ?? clientOrderId,
                    DecisionId = "BOOTSTRAP_GUARD",
                    Symbol = _tradingConfig.Symbol,
                    Side = "buy",
                    Quantity = needQty,
                    FilledPrice = last,
                    Status = "BOOTSTRAP_SUBMITTED",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                }, Aevatar.Agents.EventDirection.Down, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to publish startup guard OrderExecutedEvent to audit");
            }
        }
    }

    private static bool TryParseBaseQuote(string symbol, out string baseAsset, out string quoteAsset)
    {
        baseAsset = "";
        quoteAsset = "";
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        var s = symbol.Trim();
        var lower = s.ToLowerInvariant();

        // Normalize common shapes:
        // - cmt_btcusdt
        // - BTCUSDT_SPBL
        // - btcusdt
        lower = lower.Replace("cmt_", "", StringComparison.OrdinalIgnoreCase);
        lower = lower.Replace("_spbl", "", StringComparison.OrdinalIgnoreCase);

        // Remove separators if any.
        lower = lower.Replace("-", "").Replace("_", "");

        if (lower.EndsWith("usdt", StringComparison.OrdinalIgnoreCase))
        {
            baseAsset = lower[..^4].ToUpperInvariant();
            quoteAsset = "USDT";
            return !string.IsNullOrWhiteSpace(baseAsset);
        }

        return false;
    }

    /// <summary>
    /// Stop the trading system
    /// </summary>
    public async Task StopAsync(string reason = "Manual stop")
    {
        _logger.LogInformation("Stopping Trading System: {Reason}", reason);

        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        await dataCollector.StopCollectingAsync(reason);

        _logger.LogInformation("Trading System stopped");
    }

    /// <summary>
    /// Sync account information
    /// </summary>
    public async Task SyncAccountInfoAsync()
    {
        try
        {
            var balances = await _apiClient.GetBalancesAsync();
            var usdtBalance = balances.FirstOrDefault(b => b.Currency == "USDT");
            
            if (usdtBalance != null)
            {
                var riskManager = (RiskManagerAgent)_riskManagerActor!.GetAgent();
                riskManager.UpdateAccountInfo(
                    totalEquity: (double)usdtBalance.Balance,
                    availableBalance: (double)usdtBalance.Available,
                    currentPositionValue: 0, // Simplified handling
                    unrealizedPnl: 0);

                _logger.LogInformation(
                    "Account synced: Balance=${Balance}, Available=${Available}",
                    usdtBalance.Balance, usdtBalance.Available);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync account info");
        }
    }

    /// <summary>
    /// Get system status
    /// </summary>
    public async Task<TradingSystemStatus> GetStatusAsync()
    {
        static string NotInitialized(string name) =>
            $"{name}: not initialized (call POST /api/trading/initialize)";

        async Task<string> SafeDescAsync(IGAgentActor? actor, string name, string fallback)
        {
            if (actor == null)
                return fallback;

            try
            {
                return await actor.GetDescriptionAsync();
            }
            catch (Exception ex)
            {
                // Keep status endpoint resilient; surface errors as text instead of throwing 500.
                return $"{name}: error ({ex.GetType().Name}): {ex.Message}";
            }
        }

        var auditFallback = _auditConfig.Enabled ? NotInitialized("TradeAudit") : "TradeAudit: disabled";
        var uploaderWanted = _aiWarsConfig.Enabled || _auditConfig.RequestAiwarsUpload;
        var uploaderFallback = uploaderWanted ? NotInitialized("AiWarsUploader") : "AiWarsUploader: disabled";

        return new TradingSystemStatus
        {
            DataCollector = await SafeDescAsync(_dataCollectorActor, "DataCollector", NotInitialized("DataCollector")),
            SentimentAnalyst = await SafeDescAsync(_sentimentActor, "SentimentAnalyst", NotInitialized("SentimentAnalyst")),
            TechnicalAnalyst = await SafeDescAsync(_technicalActor, "TechnicalAnalyst", NotInitialized("TechnicalAnalyst")),
            Coordinator = await SafeDescAsync(_coordinatorActor, "Coordinator", NotInitialized("Coordinator")),
            RiskManager = await SafeDescAsync(_riskManagerActor, "RiskManager", NotInitialized("RiskManager")),
            Executor = await SafeDescAsync(_executorActor, "Executor", NotInitialized("Executor")),
            TradeAudit = await SafeDescAsync(_auditActor, "TradeAudit", auditFallback),
            AiWarsUploader = await SafeDescAsync(_aiWarsUploaderActor, "AiWarsUploader", uploaderFallback)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("System disposing");
        await _wsClient.DisposeAsync();
    }
}

/// <summary>
/// System status
/// </summary>
public record TradingSystemStatus
{
    public required string DataCollector { get; init; }
    public required string SentimentAnalyst { get; init; }
    public required string TechnicalAnalyst { get; init; }
    public required string Coordinator { get; init; }
    public required string RiskManager { get; init; }
    public required string Executor { get; init; }
    public required string TradeAudit { get; init; }
    public required string AiWarsUploader { get; init; }
}
