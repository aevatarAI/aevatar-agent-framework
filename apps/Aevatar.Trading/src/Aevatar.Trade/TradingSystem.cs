using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Trade.Agents.Analysts;
using Aevatar.Trade.Agents.Audit;
using Aevatar.Trade.AgUi;
using Aevatar.Trade.Agents.AiWars;
using Aevatar.Trade.Agents.Coordinator;
using Aevatar.Trade.Agents.Data;
using Aevatar.Trade.Agents.Execution;
using Aevatar.Trade.Agents.Policy;
using Aevatar.Trade.Agents.RiskControl;
using Aevatar.Trade.Agents.Streaming;
using Aevatar.Trade.Agents.Triggers;
using Aevatar.Trade.Infrastructure.AiWars;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Aevatar.Trade.Infrastructure.Exchanges;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;

namespace Aevatar.Trade;

/// <summary>
/// Trading system entry point
/// Responsible for creating and orchestrating all agents
/// </summary>
public class TradingSystem : IAsyncDisposable
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IWeexApiClient _apiClient;
    private readonly IExchangeClient _exchangeClient;
    private readonly IWeexAiWarsLogClient _aiWarsClient;
    private readonly TradingConfig _tradingConfig;
    private readonly AnalysisWeightConfig _analysisConfig;
    private readonly RiskControlConfig _riskConfig;
    private readonly ExchangeConfig _exchangeConfig;
    private readonly TradeAuditConfig _auditConfig;
    private readonly AiWarsLogUploadConfig _aiWarsConfig;
    private readonly DecisionTriggerConfig _triggerConfig;
    private readonly TradingPolicyConfig _policyConfig;
    private readonly DecisionEngineConfig _decisionEngineConfig;
    private readonly MarketChatConfig _marketChatConfig;
    private readonly CognitiveMeshDecisionEngine _cognitiveMeshDecisionEngine;
    private readonly LLMProvidersConfig _llmProvidersConfig;
    private readonly ITradeAgUiStreamSink _agUiStreamSink;
    private readonly ILogger<TradingSystem> _logger;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // Agent Actors
    private IGAgentActor? _dataCollectorActor;
    private IGAgentActor? _sentimentActor;
    private IGAgentActor? _technicalActor;
    private IGAgentActor? _coordinatorActor;
    private IGAgentActor? _decisionTriggerActor;
    private IGAgentActor? _policyManagerActor;
    private IGAgentActor? _riskManagerActor;
    private IGAgentActor? _executorActor;
    private IGAgentActor? _auditActor;
    private IGAgentActor? _aiWarsUploaderActor;
    private IGAgentActor? _marketChatActor;

    public TradingSystem(
        IGAgentActorFactory actorFactory,
        IWeexApiClient apiClient,
        IExchangeClient exchangeClient,
        IWeexAiWarsLogClient aiWarsClient,
        IOptions<TradingConfig> tradingConfig,
        IOptions<AnalysisWeightConfig> analysisConfig,
        IOptions<RiskControlConfig> riskConfig,
        IOptions<ExchangeConfig> exchangeConfig,
        IOptions<TradeAuditConfig> auditConfig,
        IOptions<AiWarsLogUploadConfig> aiWarsConfig,
        IOptions<DecisionTriggerConfig> triggerConfig,
        IOptions<TradingPolicyConfig> policyConfig,
        IOptions<DecisionEngineConfig> decisionEngineConfig,
        IOptions<MarketChatConfig> marketChatConfig,
        CognitiveMeshDecisionEngine cognitiveMeshDecisionEngine,
        IOptions<LLMProvidersConfig> llmProvidersConfig,
        ITradeAgUiStreamSink agUiStreamSink,
        ILogger<TradingSystem> logger)
    {
        _actorFactory = actorFactory;
        _apiClient = apiClient;
        _exchangeClient = exchangeClient;
        _aiWarsClient = aiWarsClient;
        _tradingConfig = tradingConfig.Value;
        _analysisConfig = analysisConfig.Value;
        _riskConfig = riskConfig.Value;
        _exchangeConfig = exchangeConfig.Value;
        _auditConfig = auditConfig.Value;
        _aiWarsConfig = aiWarsConfig.Value;
        _triggerConfig = triggerConfig.Value;
        _policyConfig = policyConfig.Value;
        _decisionEngineConfig = decisionEngineConfig.Value;
        _marketChatConfig = marketChatConfig.Value;
        _cognitiveMeshDecisionEngine = cognitiveMeshDecisionEngine;
        _llmProvidersConfig = llmProvidersConfig.Value;
        _agUiStreamSink = agUiStreamSink;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the trading system
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

        _logger.LogInformation("Initializing Trading System...");
        _logger.LogInformation("[Init] Build provider candidates...");

        var providerCandidates = BuildProviderCandidates();
        var disabledProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ============ Create Agent Actors ============
        _logger.LogInformation("[Init] Creating agents...");

        // 1. Data collector
        _dataCollectorActor = await _actorFactory.CreateGAgentActorAsync<DataCollectorAgent>(Guid.NewGuid().ToString(), ct);
        var dataCollector = (DataCollectorAgent)_dataCollectorActor.GetAgent();
        dataCollector.ExchangeClient = _exchangeClient;
        dataCollector.Configure(_exchangeConfig.EnableWebsocket, _exchangeConfig.EnableRestPolling);

        // 1.5 Decision trigger
        _decisionTriggerActor = await _actorFactory.CreateGAgentActorAsync<DecisionTriggerAgent>(Guid.NewGuid().ToString(), ct);
        var decisionTrigger = (DecisionTriggerAgent)_decisionTriggerActor.GetAgent();
        var triggerConfig = _policyConfig.Trigger ?? _triggerConfig;
        decisionTrigger.Configure(triggerConfig);

        // 1.6 Policy manager
        _policyManagerActor = await _actorFactory.CreateGAgentActorAsync<PolicyManagerAgent>(Guid.NewGuid().ToString(), ct);
        var policyManager = (PolicyManagerAgent)_policyManagerActor.GetAgent();
        policyManager.Configure(BuildEffectivePolicyConfig());

        // 2. Analysts
        _sentimentActor = await _actorFactory.CreateGAgentActorAsync<MarketSentimentAgent>(Guid.NewGuid().ToString(), ct);
        var sentiment = (MarketSentimentAgent)_sentimentActor.GetAgent();
        sentiment.AllowDangerousTools = false; // safe default
        sentiment.ApiClient = _apiClient;     // for funding/openInterest (avoid placeholder metrics)
        var sentimentProvider = await InitializeAgentWithFallbackAsync(
            sentiment, providerCandidates, disabledProviders, ct, "Sentiment");
        
        _technicalActor = await _actorFactory.CreateGAgentActorAsync<TechnicalAnalystAgent>(Guid.NewGuid().ToString(), ct);
        var technical = (TechnicalAnalystAgent)_technicalActor.GetAgent();
        technical.AllowDangerousTools = false; // safe default
        var technicalProvider = await InitializeAgentWithFallbackAsync(
            technical, providerCandidates, disabledProviders, ct, "Technical");

        // 2.5 Market chat (streaming UI)
        _marketChatActor = await _actorFactory.CreateGAgentActorAsync<MarketChatAgent>(Guid.NewGuid().ToString(), ct);
        var marketChat = (MarketChatAgent)_marketChatActor.GetAgent();
        marketChat.AllowDangerousTools = false;
        marketChat.StreamSink = _agUiStreamSink;
        marketChat.MarketDataClient = _exchangeClient.MarketData;
        marketChat.AccountClient = _exchangeClient.Account;
        marketChat.DefaultSymbol = _tradingConfig.Symbol;
        marketChat.Symbols = BuildActiveSymbols();
        var marketChatProvider = await InitializeAgentWithFallbackAsync(
            marketChat, providerCandidates, disabledProviders, ct, "MarketChat");
        marketChat.Configure(_marketChatConfig);

        // 3. Coordinator
        _coordinatorActor = await _actorFactory.CreateGAgentActorAsync<TradingCoordinatorAgent>(Guid.NewGuid().ToString(), ct);
        var coordinator = (TradingCoordinatorAgent)_coordinatorActor.GetAgent();
        coordinator.AllowDangerousTools = false; // coordinator should not directly trade
        var coordinatorProvider = await InitializeAgentWithFallbackAsync(
            coordinator, providerCandidates, disabledProviders, ct, "Coordinator");
        coordinator.Configure(
            _tradingConfig.MinConfidenceToTrade,
            _analysisConfig.SentimentWeight,
            _analysisConfig.TechnicalWeight,
            _analysisConfig.NewsWeight,
            _tradingConfig.ExecutionMode,
            _decisionEngineConfig.TimeoutSeconds > 0
                ? _decisionEngineConfig.TimeoutSeconds
                : 300);
        
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
        var riskProvider = await InitializeAgentWithFallbackAsync(
            riskManager, providerCandidates, disabledProviders, ct, "RiskManager");
        riskManager.Configure(
            _tradingConfig.MaxPositionPct,
            _tradingConfig.MaxTotalPositionPct,
            _tradingConfig.MaxLossPerTrade,
            _tradingConfig.MaxDailyLoss,
            _riskConfig.MaxConsecutiveLosses,
            _riskConfig.CooldownMinutes,
            _tradingConfig.MinConfidenceToTrade);
        riskManager.ExchangeClient = _exchangeClient;

        _logger.LogInformation(
            "[LLM] Providers selected: Sentiment={Sentiment}, Technical={Technical}, MarketChat={MarketChat}, Coordinator={Coordinator}, Risk={Risk}",
            sentimentProvider,
            technicalProvider,
            marketChatProvider,
            coordinatorProvider,
            riskProvider);

        // 5. Executor
        _executorActor = await _actorFactory.CreateGAgentActorAsync<ExecutorAgent>(Guid.NewGuid().ToString(), ct);
        var executor = (ExecutorAgent)_executorActor.GetAgent();
        executor.Configure(_tradingConfig.ExecutionMode);
        executor.ExchangeClient = _exchangeClient;
        
        // 6. Trade audit (optional)
        if (_auditConfig.Enabled)
        {
            _auditActor = await _actorFactory.CreateGAgentActorAsync<TradeAuditAgent>(Guid.NewGuid().ToString(), ct);
            var audit = (TradeAuditAgent)_auditActor.GetAgent();
            // Provide the effective LLM model name for AI Wars log payload (best-effort).
            var modelName = ResolveProviderModelName(coordinatorProvider);
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
        _logger.LogInformation("[Init] Linking agent hierarchy...");
        // 
        // DataCollector (Data Source)
        //      │
        //      └── Coordinator (Decision Maker)
        //              │
        //              ├── DecisionTrigger (Trigger)
        //              ├── PolicyManager (Policy)
        //              ├── SentimentAgent (Analyst)
        //              ├── TechnicalAgent (Analyst)
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
        //     └── Coordinator
        //          ├── DecisionTrigger
        //          ├── PolicyManager
        //          ├── SentimentAgent
        //          ├── TechnicalAgent
        //          ├── MarketChatAgent (Streaming)
        //          └── RiskManager
        //               └── Executor
        //                    └── TradeAudit
        //                         └── AiWarsUploader
        // ============================================================

        await ActorHierarchyCoordinator.LinkAsync(_dataCollectorActor, _coordinatorActor, _logger, ct);
        if (_decisionTriggerActor != null)
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _decisionTriggerActor, _logger, ct);
        if (_policyManagerActor != null)
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _policyManagerActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _sentimentActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _technicalActor, _logger, ct);
        if (_marketChatActor != null)
        {
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _marketChatActor, _logger, ct);
        }
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
        _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Start the trading system
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        _logger.LogInformation("Starting Trading System for {Symbol}...", _tradingConfig.Symbol);

        _logger.LogInformation("[Start] Ensure base asset minimum...");
        // Startup guard: ensure we have enough BTC value (>=10U by default) before starting the loop.
        // In Live mode this may place a small market order to top up; in DryRun it only logs.
        await EnsureMinBaseAssetValueOnStartAsync(ct);

        _logger.LogInformation("[Start] Sync account info...");
        // Sync account information (after possible bootstrap buy)
        await SyncAccountInfoAsync();

        _logger.LogInformation("[Start] Run startup risk check...");
        // Startup risk check (best-effort)
        await RequestStartupRiskCheckAsync(ct);

        _logger.LogInformation("[Start] Start data collection...");
        // Start data collection
        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        var symbols = BuildActiveSymbols();
        await dataCollector.StartCollectingAsync(
            symbols,
            _tradingConfig.Interval,
            ct);

        // Startup AI market chat (non-fatal)
        if (_marketChatActor != null)
        {
            try
            {
                var marketChat = (MarketChatAgent)_marketChatActor.GetAgent();
                await marketChat.TriggerNowAsync("SYSTEM_START", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Startup market chat failed (non-fatal)");
            }
        }

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
        var ticker = await _exchangeClient.MarketData.GetTickerAsync(_tradingConfig.Symbol, ct);
        var last = (double)ticker.LastPrice;
        if (last <= 0)
            throw new InvalidOperationException($"Startup guard failed: invalid lastPrice={ticker.LastPrice} for {_tradingConfig.Symbol}");

        var balances = await _exchangeClient.Account.GetBalancesAsync(ct);
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
        var result = await _exchangeClient.Trade.PlaceOrderAsync(new OrderRequest
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
            var balances = await _exchangeClient.Account.GetBalancesAsync();
            var usdtBalance = balances.FirstOrDefault(b => b.Currency == "USDT");

            if (usdtBalance != null)
            {
                var positions = _exchangeClient.Capabilities.SupportsPositions
                    ? await _exchangeClient.Account.GetPositionsAsync()
                    : Array.Empty<PositionInfo>();

                decimal totalNotional = 0;
                decimal totalUnrealized = 0;
                foreach (var position in positions)
                {
                    var size = Math.Abs(position.Size);
                    var notional = position.Notional
                                   ?? (position.MarkPrice.HasValue ? position.MarkPrice.Value * size : 0m);
                    totalNotional += Math.Abs(notional);
                    if (position.UnrealizedPnl.HasValue)
                        totalUnrealized += position.UnrealizedPnl.Value;
                }

                var riskManager = (RiskManagerAgent)_riskManagerActor!.GetAgent();
                riskManager.UpdateAccountInfo(
                    totalEquity: (double)usdtBalance.Balance,
                    availableBalance: (double)usdtBalance.Available,
                    currentPositionValue: (double)totalNotional,
                    unrealizedPnl: (double)totalUnrealized);

                await riskManager.ForceReduceIfFullAsync("sync-account", CancellationToken.None);

                _logger.LogInformation(
                    "Account synced: Balance=${Balance}, Available=${Available}, PositionValue=${PositionValue}",
                    usdtBalance.Balance, usdtBalance.Available, totalNotional);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync account info");
        }
    }

    // ============================================================================
    //  API Helpers (Policy / Trigger / Positions)
    // ============================================================================

    public bool SupportsPositions => _exchangeClient.Capabilities.SupportsPositions;

    public Task<TradingPolicyConfig> GetPolicyAsync()
    {
        if (_policyManagerActor == null)
            throw new InvalidOperationException("PolicyManager not initialized");

        var policyManager = (PolicyManagerAgent)_policyManagerActor.GetAgent();
        return Task.FromResult(policyManager.GetPolicySnapshot());
    }

    public async Task<TradingPolicyUpdatedEvent> UpdatePolicyAsync(
        TradingPolicyConfig policy,
        string? updatedBy,
        string? reason,
        CancellationToken ct = default)
    {
        if (_policyManagerActor == null)
            throw new InvalidOperationException("PolicyManager not initialized");

        var policyManager = (PolicyManagerAgent)_policyManagerActor.GetAgent();
        return await policyManager.UpdatePolicyAsync(policy, updatedBy, reason, ct);
    }

    public async Task<DecisionTriggerEvent> TriggerDecisionAsync(
        string symbol,
        string? reason,
        double? deltaPct = null,
        double? deltaAbs = null,
        CancellationToken ct = default)
    {
        if (_decisionTriggerActor == null)
            throw new InvalidOperationException("DecisionTrigger not initialized");

        var evt = new DecisionTriggerEvent
        {
            TriggerId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Reason = string.IsNullOrWhiteSpace(reason) ? "MANUAL" : reason.Trim(),
            DeltaPct = deltaPct ?? 0,
            DeltaAbs = deltaAbs ?? 0,
            BasePrice = 0,
            LatestPrice = 0,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await _decisionTriggerActor.PublishEventAsync(evt, Aevatar.Agents.EventDirection.Up, ct);
        return evt;
    }

    public async Task<UserChatMessageEvent> SubmitUserChatAsync(
        string message,
        string? userId = null,
        string? source = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        if (_marketChatActor == null)
            throw new InvalidOperationException("MarketChat not initialized");

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("message is required", nameof(message));

        var content = message.Trim();
        if (content.Length > 1000)
            content = content[..1000];

        var evt = new UserChatMessageEvent
        {
            MessageId = Guid.NewGuid().ToString("N"),
            UserId = userId ?? "",
            Content = content,
            Source = string.IsNullOrWhiteSpace(source) ? "UI" : source.Trim(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await _marketChatActor.PublishEventAsync(evt, Aevatar.Agents.EventDirection.Down, ct);
        return evt;
    }

    public async Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(string? symbol = null, CancellationToken ct = default)
    {
        if (!SupportsPositions)
            return Array.Empty<PositionInfo>();

        return await _exchangeClient.Account.GetPositionsAsync(symbol, ct);
    }

    private async Task RequestStartupRiskCheckAsync(CancellationToken ct)
    {
        if (_riskManagerActor == null)
            return;

        try
        {
            await _riskManagerActor.PublishEventAsync(new StartupRiskCheckRequestedEvent
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Reason = "SYSTEM_START",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, Aevatar.Agents.EventDirection.Down, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Startup risk check request failed (non-fatal)");
        }
    }

    private TradingPolicyConfig BuildEffectivePolicyConfig()
    {
        var hasPolicy =
            _policyConfig.Trading != null ||
            _policyConfig.Risk != null ||
            _policyConfig.Trigger != null ||
            _policyConfig.Analysis != null;

        if (hasPolicy)
            return _policyConfig;

        return new TradingPolicyConfig
        {
            Trading = _tradingConfig.Clone(),
            Risk = _riskConfig.Clone(),
            Trigger = _triggerConfig.Clone(),
            Analysis = _analysisConfig.Clone()
        };
    }

    private IReadOnlyList<string> BuildProviderCandidates()
    {
        if (_llmProvidersConfig.Providers.Count == 0)
        {
            throw new InvalidOperationException(
                "LLMProviders is empty. Please configure ~/.aevatar/secrets.json or appsettings.secrets.json.");
        }

        var providers = new List<string>();
        foreach (var name in _llmProvidersConfig.Providers.Keys)
        {
            if (!string.IsNullOrWhiteSpace(name))
                providers.Add(name.Trim());
        }

        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                "LLMProviders has no valid provider names. Please check your configuration.");
        }

        var ordered = new List<string>();
        var defaultName = string.IsNullOrWhiteSpace(_llmProvidersConfig.Default)
            ? null
            : _llmProvidersConfig.Default.Trim();
        string? resolvedDefault = null;

        if (!string.IsNullOrWhiteSpace(defaultName))
        {
            foreach (var name in providers)
            {
                if (string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedDefault = name;
                    break;
                }
            }

            if (resolvedDefault == null)
            {
                _logger.LogWarning(
                    "[LLM] Default provider '{Default}' not found. Falling back to configured list.",
                    defaultName);
            }
        }

        if (resolvedDefault != null)
            ordered.Add(resolvedDefault);

        foreach (var name in providers)
        {
            if (resolvedDefault != null &&
                string.Equals(name, resolvedDefault, StringComparison.OrdinalIgnoreCase))
                continue;

            ordered.Add(name);
        }

        return ordered;
    }

    private IReadOnlyList<string> BuildActiveSymbols()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_tradingConfig.Symbol))
        {
            var s = _tradingConfig.Symbol.Trim();
            if (seen.Add(s))
                list.Add(s);
        }

        foreach (var raw in _exchangeConfig.Symbols)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var s = raw.Trim();
            if (seen.Add(s))
                list.Add(s);
        }

        return list;
    }

    private async Task<string> InitializeAgentWithFallbackAsync(
        AIGAgentBase agent,
        IReadOnlyList<string> providers,
        HashSet<string> disabledProviders,
        CancellationToken ct,
        string agentName)
    {
        const int maxAttemptsPerProvider = 2;
        var attemptDelay = TimeSpan.FromMilliseconds(200);

        foreach (var provider in providers)
        {
            if (disabledProviders.Contains(provider))
                continue;

            for (var attempt = 1; attempt <= maxAttemptsPerProvider; attempt++)
            {
                try
                {
                    await agent.InitializeAsync(provider, cancellationToken: ct);
                    return provider;
                }
                catch (Exception ex) when (attempt < maxAttemptsPerProvider)
                {
                    _logger.LogWarning(
                        ex,
                        "[LLM] {Agent} init failed with provider '{Provider}' (attempt {Attempt}/{Max}). Retrying...",
                        agentName,
                        provider,
                        attempt,
                        maxAttemptsPerProvider);

                    await Task.Delay(attemptDelay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "[LLM] {Agent} init failed with provider '{Provider}'. Marking as unusable.",
                        agentName,
                        provider);

                    disabledProviders.Add(provider);
                }
            }
        }

        throw new InvalidOperationException(
            $"No usable LLM provider found for {agentName}. Check ~/.aevatar/secrets.json configuration.");
    }

    private string ResolveProviderModelName(string providerName)
    {
        if (_llmProvidersConfig.Providers.TryGetValue(providerName, out var llm))
            return string.IsNullOrWhiteSpace(llm.Model) ? providerName : llm.Model;

        foreach (var entry in _llmProvidersConfig.Providers)
        {
            if (string.Equals(entry.Key, providerName, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(entry.Value.Model)
                    ? providerName
                    : entry.Value.Model;
            }
        }

        return providerName;
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
            DecisionTrigger = await SafeDescAsync(_decisionTriggerActor, "DecisionTrigger", NotInitialized("DecisionTrigger")),
            PolicyManager = await SafeDescAsync(_policyManagerActor, "PolicyManager", NotInitialized("PolicyManager")),
            RiskManager = await SafeDescAsync(_riskManagerActor, "RiskManager", NotInitialized("RiskManager")),
            Executor = await SafeDescAsync(_executorActor, "Executor", NotInitialized("Executor")),
            TradeAudit = await SafeDescAsync(_auditActor, "TradeAudit", auditFallback),
            AiWarsUploader = await SafeDescAsync(_aiWarsUploaderActor, "AiWarsUploader", uploaderFallback)
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("System disposing");
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
    public required string DecisionTrigger { get; init; }
    public required string PolicyManager { get; init; }
    public required string RiskManager { get; init; }
    public required string Executor { get; init; }
    public required string TradeAudit { get; init; }
    public required string AiWarsUploader { get; init; }
}
