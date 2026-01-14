using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
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
    // ============================================================
    //  Lifecycle (Initialize/Start)
    //
    //  目标：UI 只需要一个 Start 按钮。
    //  - Start 会自动触发 Initialize（若未初始化）
    //  - 生命周期加锁，避免并发请求导致重复初始化/重复启动
    // ============================================================
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private bool _initialized;
    private bool _started;

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
    private readonly ILLMProviderFactory _llmProviderFactory;
    private readonly ILogger<TradingSystem> _logger;

    // Agent Actors
    private IGAgentActor? _dataCollectorActor;
    
    // NOTE:
    // - 多交易对时，分析 Agent 必须按 symbol 隔离（否则会出现：K 线缓冲混用、节流互相影响、状态互相覆盖）。
    // - 这里采用“一对一”：每个 symbol 一个 SentimentAgent + 一个 TechnicalAgent。
    private readonly Dictionary<string, IGAgentActor> _sentimentActorsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IGAgentActor> _technicalActorsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

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
        ILLMProviderFactory llmProviderFactory,
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
        _llmProviderFactory = llmProviderFactory;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the trading system
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            _logger.LogInformation("Initializing Trading System...");

        // Prefer "default" for user-secrets driven setup.
        // - Aevatar user secrets auto-normalize LLMProviders:Default to a runnable instance when possible.
        // - Using "openai-gpt4" as a hardcoded fallback often doesn't exist and leads to confusing "no analysis" states.
        var providerName = string.IsNullOrWhiteSpace(_llmProvidersConfig.Default)
            ? "default"
            : _llmProvidersConfig.Default;

        // ------------------------------------------------------------
        //  LLM “多实例/多 Key”策略（防熔断）
        //
        //  现象：所有 Agent 共用一个 providerName → 失败/限流集中爆炸 → circuit open → 全系统降级。
        //  方案：把不同 Agent（以及不同 symbol 的 analyst）分配到不同 provider 实例：
        //        - 你可以在 secrets 里配置多个 provider（不同 API key / endpoint / model）
        //        - 这里用 round-robin 分摊负载
        // ------------------------------------------------------------
        // Prefer the factory view (what is actually registered and usable right now).
        // This avoids “config says many providers, but runtime only has default” confusion.
        var llmProviderNames = _llmProviderFactory.GetAvailableProviderNames()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Fallback to raw config keys (helpful if factory is not initialized for some reason).
        if (llmProviderNames.Count == 0)
        {
            llmProviderNames = _llmProvidersConfig.Providers.Keys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (llmProviderNames.Count == 0)
        {
            llmProviderNames.Add(providerName);
        }
        else
        {
            // Keep default first for stability.
            llmProviderNames.Sort((a, b) =>
            {
                var aIsDefault = string.Equals(a, providerName, StringComparison.OrdinalIgnoreCase);
                var bIsDefault = string.Equals(b, providerName, StringComparison.OrdinalIgnoreCase);
                if (aIsDefault && !bIsDefault) return -1;
                if (!aIsDefault && bIsDefault) return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
        }

        // If configured default is not a valid provider name at runtime, fall back to the first available provider.
        // This prevents "always default" illusion when default points to a missing provider name.
        if (!_llmProviderFactory.HasProvider(providerName) && llmProviderNames.Count > 0)
        {
            var fallback = llmProviderNames[0];
            _logger.LogWarning(
                "[LLM] Default provider '{Default}' not found in factory. Falling back to '{Fallback}'.",
                providerName,
                fallback);
            providerName = fallback;
        }

        // Round-robin offset: keep distribution stable within this run, but avoid “coordinator always default”.
        var rrOffset = llmProviderNames.Count > 0
            ? (int)(DateTime.UtcNow.Ticks % llmProviderNames.Count)
            : 0;

        string PickProvider(string role, int index)
        {
            if (llmProviderNames.Count == 0) return providerName;
            var n = Math.Abs(rrOffset + index) % llmProviderNames.Count;
            var picked = llmProviderNames[n];
            // Per-pick logging can be noisy; keep it Debug.
            _logger.LogDebug("[LLM] Provider pick: role={Role}, idx={Idx} -> {Provider}", role, index, picked);
            return picked;
        }

        var intervalSeconds = TryParseIntervalSeconds(_tradingConfig.Interval) ?? 60;
        
        var symbols = GetTradingSymbols();

        // Always visible log (even if remote uses Warning+ log level).
        _logger.LogWarning(
            "[LLM] Provider assignment enabled: default={Default}, providers=[{Providers}], rrOffset={Offset}, intervalSeconds={IntervalSeconds}, symbols={SymbolCount}",
            providerName,
            string.Join(", ", llmProviderNames),
            rrOffset,
            intervalSeconds,
            symbols.Count);

        // Also log the role assignment summary once (high signal, low spam).
        _logger.LogWarning(
            "[LLM] Role assignment: coordinator={Coordinator}, risk={Risk}, firstSymbolSentiment={S1}, firstSymbolTechnical={T1}",
            PickProvider("coordinator", 0),
            PickProvider("risk", llmProviderNames.Count > 1 ? 1 : 0),
            symbols.Count > 0 ? PickProvider("sentiment", 0) : "(n/a)",
            symbols.Count > 0 ? PickProvider("technical", 1) : "(n/a)");

        // Always visible: per-symbol assignment table (helps debug “no pick logs” in remote envs).
        if (symbols.Count > 0)
        {
            var lines = new List<string>();
            for (var i = 0; i < symbols.Count; i++)
            {
                var sym = symbols[i];
                var sPick = PickProvider("sentiment", i * 2);
                var tPick = PickProvider("technical", i * 2 + 1);
                lines.Add($"{sym}: sentiment={sPick}, technical={tPick}");
            }

            _logger.LogWarning(
                "[LLM] Assignment table:\n{Table}",
                string.Join("\n", lines));
        }

        // ============ Create Agent Actors ============

        // 1. Data collector
        _dataCollectorActor = await _actorFactory.CreateGAgentActorAsync<DataCollectorAgent>(Guid.NewGuid().ToString(), ct);
        var dataCollector = (DataCollectorAgent)_dataCollectorActor.GetAgent();
        dataCollector.ApiClient = _apiClient;
        dataCollector.WebSocketClient = _wsClient;

        // 2. Analysts (per-symbol)
        _sentimentActorsBySymbol.Clear();
        _technicalActorsBySymbol.Clear();
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            // Sentiment
            var sentimentActor = await _actorFactory.CreateGAgentActorAsync<MarketSentimentAgent>($"sentiment-{symbol}", ct);
            var sentiment = (MarketSentimentAgent)sentimentActor.GetAgent();
            sentiment.TargetSymbol = symbol;
            sentiment.AllowDangerousTools = false; // safe default
            sentiment.ApiClient = _apiClient;     // for funding/openInterest (avoid placeholder metrics)
            sentiment.Configure(intervalSeconds);
            await sentiment.InitializeAsync(PickProvider("sentiment", i * 2), cancellationToken: ct);
            _sentimentActorsBySymbol[symbol] = sentimentActor;

            // Technical
            var technicalActor = await _actorFactory.CreateGAgentActorAsync<TechnicalAnalystAgent>($"technical-{symbol}", ct);
            var technical = (TechnicalAnalystAgent)technicalActor.GetAgent();
            technical.TargetSymbol = symbol;
            technical.AllowDangerousTools = false; // safe default
            technical.Configure(intervalSeconds);
            await technical.InitializeAsync(PickProvider("technical", i * 2 + 1), cancellationToken: ct);
            _technicalActorsBySymbol[symbol] = technicalActor;
        }

        // 3. Coordinator
        _coordinatorActor = await _actorFactory.CreateGAgentActorAsync<TradingCoordinatorAgent>(Guid.NewGuid().ToString(), ct);
        var coordinator = (TradingCoordinatorAgent)_coordinatorActor.GetAgent();
        coordinator.AllowDangerousTools = false; // coordinator should not directly trade
        var coordinatorProvider = PickProvider("coordinator", 0);
        await coordinator.InitializeAsync(coordinatorProvider, cancellationToken: ct);
        coordinator.Configure(
            _tradingConfig.MinConfidenceToTrade,
            _analysisConfig.SentimentWeight,
            _analysisConfig.TechnicalWeight,
            _analysisConfig.NewsWeight,
            _tradingConfig.ExecutionMode,
            _decisionEngineConfig.TimeoutSeconds,
            decisionMinIntervalSeconds: intervalSeconds);
        
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
        await riskManager.InitializeAsync(PickProvider("risk", llmProviderNames.Count > 1 ? 1 : 0), cancellationToken: ct);
          // If Coordinator uses CognitiveMesh, let RiskManager also use CognitiveMesh risk workflow.
          if (string.Equals(_decisionEngineConfig.Mode, "CognitiveMesh", StringComparison.OrdinalIgnoreCase) &&
              !string.IsNullOrWhiteSpace(_decisionEngineConfig.CognitiveMeshBaseUrl))
          {
              riskManager.CognitiveMeshBaseUrl = _decisionEngineConfig.CognitiveMeshBaseUrl;
              riskManager.CognitiveMeshStrategy = string.IsNullOrWhiteSpace(_decisionEngineConfig.CognitiveMeshStrategy)
                  ? "Cognitive"
                  : _decisionEngineConfig.CognitiveMeshStrategy;
              // Risk workflow name is fixed for now (Cognitive side defines it).
              riskManager.CognitiveRiskWorkflow = "trade-risk";
          }
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
            var modelName = _llmProvidersConfig.Providers.TryGetValue(coordinatorProvider, out var llm)
                ? llm.Model
                : coordinatorProvider;
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
        
        // Coordinator -> Analysts (per-symbol)
        foreach (var actor in _sentimentActorsBySymbol.Values)
        {
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, actor, _logger, ct);
        }
        foreach (var actor in _technicalActorsBySymbol.Values)
        {
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, actor, _logger, ct);
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
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// Start the trading system
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // One-click start:
        // - If not initialized, initialize first.
        if (!_initialized)
        {
            await InitializeAsync(ct);
        }

        if (_started)
            return;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_started)
                return;

            var symbols = GetTradingSymbols();
            _logger.LogInformation(
                "Starting Trading System for {Count} symbols: {Symbols}",
                symbols.Count,
                string.Join(", ", symbols));

        // Startup guard: ensure we have enough BTC value (>=10U by default) before starting the loop.
        // In Live mode this may place a small market order to top up; in DryRun it only logs.
        await EnsureMinBaseAssetValueOnStartAsync(ct);

        // Sync account information (after possible bootstrap buy)
        await SyncAccountInfoAsync();

        // Start data collection
        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        await dataCollector.StartCollectingAsync(
            symbols,
            _tradingConfig.Interval,
            ct);

            _started = true;
            _logger.LogInformation("Trading System started");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private static int? TryParseIntervalSeconds(string? interval)
    {
        // Accept common formats:
        // - "2m" -> 120
        // - "30s" -> 30
        // - "1h" -> 3600
        // - "2min" / "2mins" / "2minute(s)" best-effort
        if (string.IsNullOrWhiteSpace(interval))
            return null;

        var s = interval.Trim().ToLowerInvariant();
        if (s.Length < 2)
            return null;

        // Extract numeric prefix
        var i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
            i++;
        if (i == 0)
            return null;

        if (!double.TryParse(s[..i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return null;
        if (n <= 0)
            return null;

        var unit = s[i..].Trim();
        if (unit == "s" || unit == "sec" || unit == "secs" || unit == "second" || unit == "seconds")
            return (int)Math.Round(n);
        if (unit == "m" || unit == "min" || unit == "mins" || unit == "minute" || unit == "minutes")
            return (int)Math.Round(n * 60);
        if (unit == "h" || unit == "hr" || unit == "hrs" || unit == "hour" || unit == "hours")
            return (int)Math.Round(n * 3600);

        return null;
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

        // Prefer BTC for startup guard (historical demo requirement).
        var symbols = GetTradingSymbols();
        var guardSymbol =
            symbols.FirstOrDefault(s => s.Equals("cmt_btcusdt", StringComparison.OrdinalIgnoreCase))
            ?? symbols.FirstOrDefault()
            ?? _tradingConfig.Symbol;

        if (!TryParseBaseQuote(guardSymbol, out var baseAsset, out var quoteAsset))
        {
            _logger.LogWarning(
                "Startup guard skipped: cannot parse base/quote from symbol={Symbol}",
                guardSymbol);
            return;
        }

        if (!string.Equals(quoteAsset, "USDT", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Startup guard skipped: quoteAsset={Quote} is not USDT (symbol={Symbol})",
                quoteAsset, guardSymbol);
            return;
        }

        // Always read current price from WEEX to avoid stale assumptions.
        var ticker = await _apiClient.GetTickerAsync(guardSymbol, ct);
        var last = (double)ticker.LastPrice;
        if (last <= 0)
            throw new InvalidOperationException($"Startup guard failed: invalid lastPrice={ticker.LastPrice} for {guardSymbol}");

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
            Symbol = guardSymbol,
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
                        Symbol = guardSymbol,
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
                    Symbol = guardSymbol,
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

        if (_dataCollectorActor != null)
        {
            var dataCollector = (DataCollectorAgent)_dataCollectorActor.GetAgent();
            await dataCollector.StopCollectingAsync(reason);
        }

        _started = false;
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
            $"{name}: not initialized (call POST /api/trading/start)";

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
        
        async Task<string> SafeGroupDescAsync(
            IReadOnlyDictionary<string, IGAgentActor> actors,
            string groupName,
            string fallback)
        {
            if (actors.Count == 0)
                return fallback;

            // Avoid overly-long status strings. Show a few samples; the full list is available via /api/agents.
            const int maxSamples = 2;
            var parts = new List<string>(capacity: maxSamples);
            foreach (var actor in actors.Values.Take(maxSamples))
            {
                parts.Add(await SafeDescAsync(actor, groupName, $"{groupName}: error"));
            }

            var more = actors.Count > maxSamples ? $" (+{actors.Count - maxSamples} more)" : "";
            return $"{groupName}: {actors.Count} agents{more} | " + string.Join(" | ", parts);
        }

        var auditFallback = _auditConfig.Enabled ? NotInitialized("TradeAudit") : "TradeAudit: disabled";
        var uploaderWanted = _aiWarsConfig.Enabled || _auditConfig.RequestAiwarsUpload;
        var uploaderFallback = uploaderWanted ? NotInitialized("AiWarsUploader") : "AiWarsUploader: disabled";

        return new TradingSystemStatus
        {
            DataCollector = await SafeDescAsync(_dataCollectorActor, "DataCollector", NotInitialized("DataCollector")),
            SentimentAnalyst = await SafeGroupDescAsync(
                _sentimentActorsBySymbol,
                "SentimentAnalyst",
                NotInitialized("SentimentAnalyst")),
            TechnicalAnalyst = await SafeGroupDescAsync(
                _technicalActorsBySymbol,
                "TechnicalAnalyst",
                NotInitialized("TechnicalAnalyst")),
            Coordinator = await SafeDescAsync(_coordinatorActor, "Coordinator", NotInitialized("Coordinator")),
            RiskManager = await SafeDescAsync(_riskManagerActor, "RiskManager", NotInitialized("RiskManager")),
            Executor = await SafeDescAsync(_executorActor, "Executor", NotInitialized("Executor")),
            TradeAudit = await SafeDescAsync(_auditActor, "TradeAudit", auditFallback),
            AiWarsUploader = await SafeDescAsync(_aiWarsUploaderActor, "AiWarsUploader", uploaderFallback)
        };
    }

    private IReadOnlyList<string> GetTradingSymbols()
    {
        // ------------------------------------------------------------
        //  多交易对支持（AI Wars）
        //  - 优先使用 Trading:Symbols（列表）
        //  - 兼容单交易对 Trading:Symbol（历史字段）
        //
        //  风险：
        //  - 交易非允许交易对会被取消资格，所以这里做白名单过滤并输出告警。
        // ------------------------------------------------------------
        var raw = new List<string>();
        if (_tradingConfig.Symbols != null && _tradingConfig.Symbols.Count > 0)
        {
            raw.AddRange(_tradingConfig.Symbols);
        }
        else if (!string.IsNullOrWhiteSpace(_tradingConfig.Symbol))
        {
            raw.Add(_tradingConfig.Symbol);
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmt_btcusdt",
            "cmt_ethusdt",
            "cmt_solusdt",
            "cmt_dogeusdt",
            "cmt_xrpusdt",
            "cmt_adausdt",
            "cmt_bnbusdt",
            "cmt_ltcusdt"
        };

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in raw)
        {
            var sym = (s ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sym))
                continue;

            if (!allowed.Contains(sym))
            {
                _logger.LogWarning(
                    "Trading symbol '{Symbol}' is not in AI Wars allowed list; skipping to avoid disqualification.",
                    sym);
                continue;
            }

            if (seen.Add(sym))
                result.Add(sym);
        }

        if (result.Count == 0)
        {
            // Failsafe: do not start trading with an unknown/disallowed symbol set.
            result.Add("cmt_btcusdt");
        }

        return result;
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
