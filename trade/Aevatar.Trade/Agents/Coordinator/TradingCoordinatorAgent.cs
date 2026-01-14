using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;

namespace Aevatar.Trade.Agents.Coordinator;

/// <summary>
/// Trading coordinator agent (Chief trading decision maker)
/// Responsibilities: Synthesize opinions from all analysts and make final trading decisions
/// </summary>
public partial class TradingCoordinatorAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are the chief trading decision maker.
        
        Mode: **Aggressive Micro-Scalping**
        - Goal: take many small, low-risk trades; capture tiny profits quickly.
        - Profit target: small but real — enough to cover fees and leave a small edge.
        - Bias: if there is no strong bearish evidence, prefer taking a tiny long entry rather than doing nothing.
        - HOLD should be rare: only when signals are extremely conflicting / price is chaotic / data is missing AND risk is unclear.

        【Your Analysis Team】
        1. Market Sentiment Analyst - Provides market sentiment scores and trend judgments
        2. Technical Analyst - Provides technical indicator analysis and trend judgments
        3. News Analyst - Provides news event impact assessments

        【Decision Framework】

        1. Micro-Scalp Entry Logic (be proactive)
           - If Technical is not clearly BEARISH and Sentiment is not extremely negative: lean BUY with small size.
           - If Technical is clearly BEARISH or funding/open-interest suggests crowded longs: lean SELL (or HOLD if unsure).
           - If analysis is missing: treat missing parts as NEUTRAL and still allow a tiny probe trade.

        2. Priority Rules
           - Major news events > Technical analysis > Sentiment analysis
           - Consider contrarian operations during extreme sentiment (fear/greed)
           - Follow trend when clear, wait and see during consolidation

        3. Confidence Adjustment
           - Multiple signal resonance: Increase confidence
           - Signal divergence: Decrease confidence
           - Recent accuracy rate: Dynamic adjustment

        【Output Format】
        Please strictly output in the following JSON format:
        {
            "direction": "<BUY|SELL|HOLD>",
            "confidence": <integer 1-100>,
            "position_pct": <suggested position percentage, 0-30>,
            "reasoning": {
                "sentiment_factor": "<sentiment factor summary>",
                "technical_factor": "<technical factor summary>",
                "news_factor": "<news factor summary>",
                "final_logic": "<final decision logic>"
            },
            "summary": "<one-sentence decision summary>"
        }

        【Scalping Parameters (must follow)】
        - position_pct: prefer 0.5%~2.0% for probe trades; only go larger if signals align strongly.
        - If you choose BUY/SELL, do NOT output very low confidence. For micro-scalps, output confidence >= 50 unless truly unsure.
        - Your decision should be actionable: direction should usually be BUY or SELL, not HOLD.
        
        【Risk Awareness (still required)】
        - Keep trades small; avoid large exposure.
        - Avoid HOLD-by-default; small probes are acceptable.
        """;

    // ============ State ============

    private readonly CoordinatorState _coordState = new();
    private readonly object _stateLock = new();
    
    // Configuration
    private int _minConfidenceToTrade = 60;
    private double _sentimentWeight = 0.3;
    private double _technicalWeight = 0.4;
    private double _newsWeight = 0.3;
    private string _executionMode = "DryRun";
    // Decision timeout is a *safety valve* (avoid deadlock), not a "fast fail".
    // Default to a large value; user can tune via DecisionEngine:TimeoutSeconds.
    private int _decisionTimeoutSeconds = 300;

    // ---------------------------------------------------------------------
    //  Decision frequency / re-entrancy guard
    //
    //  背景：
    //  - 默认实现仅在分析事件到达时尝试决策，并且有 30s 节流。
    //  - 在 5m K 线场景下，技术面更新很慢，会导致“半小时没有任何决策”的错觉。
    //
    //  目标：
    //  - 高频：允许每秒尝试一次（用户明确要求 LLM 调用不设上限）。
    //  - 稳定：同一时间只跑一个决策（避免并发堆积导致延迟爆炸）。
    // ---------------------------------------------------------------------
    // ============================================================================
    //  决策节流（非常关键）
    //
    //  现象：Trading:Interval=2m 你以为“两分钟决策一次”，但系统仍会每秒 poll ticker，
    //       且每次 tick 都会触发 TryMakeDecisionAsync -> 造成“看起来像 2s 一次”的错觉。
    //
    //  原则：
    //  - 行情采样可以高频（为了风控/限价 sizing），但“LLM 决策”必须有明确节流。
    //  - 默认：跟 Trading:Interval 对齐（例如 2m -> 120s），并允许通过 Configure 覆盖。
    // ============================================================================
    private int _decisionMinIntervalSeconds = 60;
    private int _decisionRunning;
    private const int NoStrategyExplainIntervalSeconds = 60;
    private DateTime _decisionInFlightStartUtc = DateTime.MinValue;

    // Latest market snapshot (from DataCollector) for pricing / order placement.
    private readonly ConcurrentDictionary<string, MarketTickEvent> _latestTickBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    // Latest analysis snapshots (per symbol).
    // - 多交易对下，如果用单一 LatestSentiment/LatestTechnical，会“互相覆盖”，导致只对最后一个 symbol 决策。
    private readonly ConcurrentDictionary<string, MarketSentimentAnalysisEvent> _latestSentimentBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TechnicalAnalysisEvent> _latestTechnicalBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, NewsImpactAnalysisEvent> _latestNewsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    // Per-symbol throttles (avoid one symbol starving all others).
    private readonly ConcurrentDictionary<string, DateTime> _lastDecisionUtcBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastNoStrategyExplainUtcBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    // Multi-symbol decision scheduler (completion-driven):
    // - 同一 symbol 的新 tick/分析到达时，只标记 pending（合并），不堆积队列。
    // - 决策 worker 单飞：上一轮 LLM 未完成时，不启动下一轮；完成后如果 pending，则立刻继续。
    // - 仍保留最小间隔 _decisionMinIntervalSeconds，避免“LLM 一返回就立刻无限加速”导致限流/熔断。
    private readonly ConcurrentDictionary<string, byte> _pendingDecisionBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _decisionSignal = new(0, int.MaxValue);

    /// <summary>
    /// Optional decision engine override.
    /// - null: use direct LLM (ChatAsync)
    /// - non-null: delegate to external engine (e.g., Cognitive Mesh)
    /// </summary>
    public ITradingDecisionEngine? DecisionEngine { get; set; }

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _coordState.AgentId = Id.ToString();
        Logger.LogInformation("[Coordinator] Activated: {AgentId}", _coordState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        lock (_stateLock)
        {
            return Task.FromResult(
                $"TradingCoordinator: Decisions={_coordState.DecisionsMade}, " +
                $"Approved={_coordState.DecisionsApproved}, " +
                $"Rejected={_coordState.DecisionsRejected}");
        }
    }

    /// <summary>
    /// Configure decision parameters
    /// </summary>
    public void Configure(
        int minConfidence = 60,
        double sentimentWeight = 0.3,
        double technicalWeight = 0.4,
        double newsWeight = 0.3,
        TradeExecutionMode executionMode = TradeExecutionMode.DryRun,
        int decisionTimeoutSeconds = 300,
        int? decisionMinIntervalSeconds = null)
    {
        _minConfidenceToTrade = minConfidence;
        _sentimentWeight = sentimentWeight;
        _technicalWeight = technicalWeight;
        _newsWeight = newsWeight;
        _executionMode = executionMode.ToString();
        _decisionTimeoutSeconds = Math.Clamp(decisionTimeoutSeconds, 3, 3600);
        if (decisionMinIntervalSeconds.HasValue && decisionMinIntervalSeconds.Value > 0)
        {
            _decisionMinIntervalSeconds = Math.Clamp(decisionMinIntervalSeconds.Value, 1, 24 * 60 * 60);
        }

        Logger.LogInformation(
            "[Coordinator] Configured: MinConf={MinConf}, Weights=[S:{S}, T:{T}, N:{N}], Timeout={Timeout}s, DecisionMinInterval={Interval}s",
            minConfidence, sentimentWeight, technicalWeight, newsWeight, _decisionTimeoutSeconds, _decisionMinIntervalSeconds);
    }

    // ============ Event Handlers ============

    /// <summary>
    /// Handle market ticks (price snapshot for sizing / limit price).
    /// </summary>
    [EventHandler]
    public Task HandleMarketTick(MarketTickEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.Symbol))
        {
            _latestTickBySymbol[evt.Symbol] = evt;
        }

        // =========================================================================
        // 关键修复：不要在事件处理链路里 await LLM 决策
        //
        // 原因：
        // - LocalMessageStream 对“单个 Agent 的 stream”是串行处理（await handler 完成才处理下一条）
        // - 如果这里 await 一个慢/卡住的 LLM 调用：
        //   1) Coordinator 自己会“卡死”，后续 tick/analysis 都进不来
        //   2) 更致命：Broadcast 的 Down 传播发生在 handler 返回之后，Analyst/Risk/Executor 也收不到事件
        // - 结果就是你看到的：trade-audit 只剩下一个 Cycle，之后全停
        //
        // 解决：
        // - 触发决策改为 fire-and-forget，让事件处理快速返回，传播不阻塞
        // - 内部用 _decisionRunning 做 single-flight，避免并发决策风暴
        // =========================================================================
        _ = TryMakeDecisionAsync(evt.Symbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle market sentiment analysis results
    /// </summary>
    [EventHandler]
    public Task HandleSentimentAnalysis(MarketSentimentAnalysisEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.Symbol))
        {
            _latestSentimentBySymbol[evt.Symbol] = evt;
        }

        lock (_stateLock)
        {
            _coordState.LatestSentiment = evt;
        }
        Logger.LogDebug(
            "[Coordinator] Received sentiment: Score={Score}, Trend={Trend}",
            evt.SentimentScore, evt.SentimentTrend);

        // ------------------------------------------------------------
        //  Observability: fan-out analysis to downstream chain
        //
        //  Why:
        //  - Sentiment/Technical are published Up to Coordinator.
        //  - TradeAudit is attached under Executor (Coordinator -> Risk -> Executor -> Audit).
        //  - If we don't forward analysis Down, Audit cannot build "Signal Snapshot"
        //    (it will show N/A forever).
        //
        //  Rule:
        //  - Fire-and-forget (never block coordinator stream).
        // ------------------------------------------------------------
        _ = PublishAsync(evt, Aevatar.Agents.EventDirection.Down);

        _ = TryMakeDecisionAsync(evt.Symbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle technical analysis results
    /// </summary>
    [EventHandler]
    public Task HandleTechnicalAnalysis(TechnicalAnalysisEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.Symbol))
        {
            _latestTechnicalBySymbol[evt.Symbol] = evt;
        }

        lock (_stateLock)
        {
            _coordState.LatestTechnical = evt;
        }
        Logger.LogDebug(
            "[Coordinator] Received technical: Trend={Trend}, Signal={Signal}",
            evt.TrendDirection, evt.Signal);

        // Observability: forward latest analysis to audit chain (see HandleSentimentAnalysis).
        _ = PublishAsync(evt, Aevatar.Agents.EventDirection.Down);

        _ = TryMakeDecisionAsync(evt.Symbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle news impact analysis results
    /// </summary>
    [EventHandler]
    public Task HandleNewsAnalysis(NewsImpactAnalysisEvent evt)
    {
        // News can affect multiple symbols; cache per symbol for decision prompts.
        if (evt.AffectedSymbols != null)
        {
            foreach (var s in evt.AffectedSymbols)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    _latestNewsBySymbol[s] = evt;
            }
        }

        lock (_stateLock)
        {
            _coordState.LatestNews = evt;
        }
        Logger.LogDebug(
            "[Coordinator] Received news: Impact={Impact}, Level={Level}",
            evt.ImpactType, evt.ImpactLevel);

        // Observability: forward latest analysis to audit chain (see HandleSentimentAnalysis).
        _ = PublishAsync(evt, Aevatar.Agents.EventDirection.Down);

        _ = TryMakeDecisionAsync(evt.AffectedSymbols?.FirstOrDefault() ?? "");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle trade approval events (for statistics)
    /// </summary>
    [EventHandler]
    public Task HandleTradeApproved(ApprovedTradeEvent evt)
    {
        lock (_stateLock)
        {
            _coordState.DecisionsApproved++;
        }
        Logger.LogInformation(
            "[Coordinator] Decision approved: {DecisionId} -> {Side} {Symbol}",
            evt.DecisionId, evt.Side, evt.Symbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle trade rejection events (for statistics)
    /// </summary>
    [EventHandler]
    public Task HandleTradeRejected(TradeRejectedEvent evt)
    {
        lock (_stateLock)
        {
            _coordState.DecisionsRejected++;
        }
        Logger.LogWarning(
            "[Coordinator] Decision rejected: {DecisionId}, Reason: {Reason}",
            evt.DecisionId, evt.RejectionReason);
        return Task.CompletedTask;
    }

    // ============ Decision Logic ============

    /// <summary>
    /// Attempt to make a trading decision
    /// </summary>
    private Task TryMakeDecisionAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Task.CompletedTask;

        // Coalesce: mark as pending (latest snapshot wins).
        _pendingDecisionBySymbol[symbol] = 1;
        _decisionSignal.Release();

        // Fire-and-forget:
        // - event handler must return fast so Down propagation is not blocked
        // - the worker uses _decisionRunning single-flight to avoid concurrent LLM calls
        _ = RunDecisionLoopAsync();
        return Task.CompletedTask;
    }

    private async Task RunDecisionLoopAsync()
    {
        if (Interlocked.Exchange(ref _decisionRunning, 1) == 1)
            return;

        try
        {
            _decisionInFlightStartUtc = DateTime.UtcNow;

            while (true)
            {
                // 1) Pick ONE eligible symbol to decide now.
                //    - If all pending symbols are within min-interval, wait until earliest allowed time or a new signal.
                var now = DateTime.UtcNow;
                string? picked = null;
                TimeSpan? earliestWait = null;

                foreach (var kv in _pendingDecisionBySymbol)
                {
                    var symbol = kv.Key;
                    if (string.IsNullOrWhiteSpace(symbol))
                    {
                        _pendingDecisionBySymbol.TryRemove(symbol, out _);
                        continue;
                    }

                    if (!HasSufficientData(symbol, out var noDataReason))
                    {
                        _pendingDecisionBySymbol.TryRemove(symbol, out _);
                        MaybePublishNoStrategy(symbol, noDataReason);
                        continue;
                    }

                    if (_lastDecisionUtcBySymbol.TryGetValue(symbol, out var lastUtc))
                    {
                        var nextAllowed = lastUtc.AddSeconds(_decisionMinIntervalSeconds);
                        if (nextAllowed > now)
                        {
                            var wait = nextAllowed - now;
                            earliestWait = earliestWait == null ? wait : (wait < earliestWait ? wait : earliestWait);
                            continue;
                        }
                    }

                    picked = symbol;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(picked))
                {
                    // Remove pending before executing to avoid re-entrancy storms.
                    _pendingDecisionBySymbol.TryRemove(picked, out _);
                    await MakeDecisionAsync(picked);
                    _lastDecisionUtcBySymbol[picked] = DateTime.UtcNow;
                    continue;
                }

                // 2) No eligible symbol right now.
                if (_pendingDecisionBySymbol.IsEmpty)
                {
                    // Drain extra signals (best-effort) to avoid a busy restart when idle.
                    while (_decisionSignal.CurrentCount > 0)
                        _ = _decisionSignal.Wait(0);
                    break;
                }

                // 3) Pending exists but all are within min-interval: wait until earliest allowed OR a new incoming signal.
                var waitFor = earliestWait ?? TimeSpan.FromMilliseconds(200);
                waitFor = waitFor < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : waitFor;
                await _decisionSignal.WaitAsync(waitFor);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _decisionRunning, 0);
            _decisionInFlightStartUtc = DateTime.MinValue;
        }
    }

    private void MaybePublishNoStrategy(string symbol, string reason)
    {
        var now = DateTime.UtcNow;
        var last = _lastNoStrategyExplainUtcBySymbol.TryGetValue(symbol, out var t) ? t : DateTime.MinValue;
        if ((now - last).TotalSeconds < NoStrategyExplainIntervalSeconds)
            return;

        _lastNoStrategyExplainUtcBySymbol[symbol] = now;
        _ = PublishNoStrategyCycleAsync(symbol, reason);
    }

    private bool HasSufficientData(string symbol, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(symbol))
        {
            reason = "empty symbol";
            return false;
        }

        // Need a price snapshot to size positions and place limit orders reliably.
        if (!_latestTickBySymbol.TryGetValue(symbol, out var tick) || tick.Price <= 0)
        {
            reason = $"no market tick snapshot for {symbol} (ticker polling may be failing)";
            return false;
        }

        // Aggressive micro-scalp mode:
        // - We allow decisions even when analysts haven't produced reports yet.
        // - Missing analysis will be treated as NEUTRAL by the prompt.
        return true;
    }

    private async Task PublishNoStrategyCycleAsync(string symbol, string reason)
    {
        try
        {
            var cycleId = Guid.NewGuid().ToString("N")[..16];
            var decisionId = $"NO_STRATEGY_{cycleId}";

            await PublishAsync(new DecisionCycleStartedEvent
            {
                CycleId = cycleId,
                Symbol = symbol,
                Trigger = $"NO_STRATEGY: {TrimReason(reason)}",
                CoordinatorId = _coordState.AgentId,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            // Publish a synthetic HOLD decision so the audit log can show a concrete reason.
            await PublishAsync(new TradingDecisionEvent
            {
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 0,
                SuggestedPositionPct = 0,
                SuggestedPrice = _latestTickBySymbol.TryGetValue(symbol, out var t) ? t.Price : 0,
                SentimentSummary = "",
                TechnicalSummary = "",
                NewsSummary = "",
                Reasoning = TrimReason(reason),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await PublishAsync(new DecisionCycleCompletedEvent
            {
                CycleId = cycleId,
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 0,
                Executed = false,
                ExecutionMode = _executionMode,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Coordinator] Failed to publish NO_STRATEGY cycle");
        }
    }

    private async Task PublishTimeoutCycleAsync(string cycleId, string symbol, double currentPrice, string reason)
    {
        try
        {
            var decisionId = $"TIMEOUT_{cycleId}";
            var r = TrimReason(reason);

            // Publish a synthetic HOLD decision so audit has a concrete explanation.
            await PublishAsync(new TradingDecisionEvent
            {
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 0,
                SuggestedPositionPct = 0,
                SuggestedPrice = currentPrice > 0 ? currentPrice : 0,
                SentimentSummary = "",
                TechnicalSummary = "",
                NewsSummary = "",
                Reasoning = r,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await PublishAsync(new DecisionCycleCompletedEvent
            {
                CycleId = cycleId,
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 0,
                Executed = false,
                ExecutionMode = _executionMode,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Coordinator] Failed to publish timeout cycle");
        }
    }

    private async Task MakeDecisionAsync(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            symbol = "UNKNOWN";

        var prompt = BuildDecisionPrompt(symbol);
        var cycleId = Guid.NewGuid().ToString("N")[..16];

        // Pricing snapshot (best-effort). This will be attached to the decision so downstream agents can size orders.
        _latestTickBySymbol.TryGetValue(symbol, out var tick);
        var currentPrice = tick?.Price ?? 0d;

        await PublishAsync(new DecisionCycleStartedEvent
        {
            CycleId = cycleId,
            Symbol = symbol,
            Trigger = "ANALYSIS_UPDATE",
            CoordinatorId = _coordState.AgentId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        try
        {
            var engine = DecisionEngine ?? new DirectLlmDecisionEngine(async (p, ct) =>
            {
                var req = ChatRequest.Create(p);
                // 快速决策：低温 + 限制输出长度，强迫尽快返回结构化 JSON
                req.SetTemperatureIfNotSet(0.2);
                req.SetMaxTokensIfNotSet(420);
                var chat = await ChatWithFailoverAsync(req, ct);
                return chat.Content ?? string.Empty;
            });

            // ============================================================
            //  LLM reliability guardrails
            //
            //  Why:
            //  - If the provider hangs (network stall / upstream outage), the whole trading loop
            //    appears "no strategy forever" and the markdown log stops updating.
            //
            //  Rule:
            //  - Always finish the cycle (publish DecisionCycleCompleted) within timeout.
            // ============================================================
            var timeout = TimeSpan.FromSeconds(_decisionTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);

            string raw;
            try
            {
                var task = engine.GetDecisionJsonAsync(prompt, cycleId, timeoutCts.Token);
                raw = await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                timeoutCts.Cancel();
                await PublishTimeoutCycleAsync(cycleId, symbol, currentPrice, $"llm-timeout>{_decisionTimeoutSeconds}s");
                return;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                await PublishTimeoutCycleAsync(cycleId, symbol, currentPrice, $"llm-timeout>{_decisionTimeoutSeconds}s");
                return;
            }

            var decision = ParseDecisionResponse(raw, symbol);
            if (currentPrice > 0)
            {
                decision.SuggestedPrice = currentPrice;
            }

            // Update state
            lock (_stateLock)
            {
                _coordState.LastDecision = decision.Direction;
                _coordState.DecisionsMade++;
                _coordState.LastDecisionTime = Timestamp.FromDateTime(DateTime.UtcNow);
            }

            // Check confidence threshold
            var forwardedToRisk = decision.Direction != "HOLD" && decision.Confidence >= _minConfidenceToTrade;

            // Publish decision for observability (even if it's HOLD / below threshold).
            // RiskManager will fast-reject HOLD/low-confidence without calling its own LLM.
            await PublishAsync(decision);

            if (!forwardedToRisk)
            {
                Logger.LogInformation(
                    "[Coordinator] Decision: HOLD (Confidence={Conf} < {Min} or explicit HOLD)",
                    decision.Confidence, _minConfidenceToTrade);

                await PublishAsync(new DecisionCycleCompletedEvent
                {
                    CycleId = cycleId,
                    DecisionId = decision.DecisionId,
                    Symbol = symbol,
                    Direction = decision.Direction,
                    Confidence = decision.Confidence,
                    Executed = false,
                    ExecutionMode = _executionMode,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
                return;
            }

            Logger.LogInformation(
                "[Coordinator] Decision published: {Direction} {Symbol}, Confidence={Conf}%",
                decision.Direction, symbol, decision.Confidence);

            await PublishAsync(new DecisionCycleCompletedEvent
            {
                CycleId = cycleId,
                DecisionId = decision.DecisionId,
                Symbol = symbol,
                Direction = decision.Direction,
                Confidence = decision.Confidence,
                Executed = true,
                ExecutionMode = _executionMode,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Aevatar.Agents.AI.Abstractions.CircuitBreakerOpenException cbEx)
        {
            // ------------------------------------------------------------
            //  LLM fallback (AI-unavailable must not mean "no trading forever")
            //
            //  Philosophy:
            //  - Keep the system alive and making decisions.
            //  - When AI is down, switch to a tiny deterministic micro-scalp policy.
            // ------------------------------------------------------------
            Logger.LogWarning(cbEx, "[Coordinator] LLM circuit OPEN for {Symbol}. Using fallback decision policy.", symbol);
            await PublishFallbackDecisionCycleAsync(
                cycleId,
                symbol,
                currentPrice,
                reason: $"llm-circuit-open: {cbEx.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Coordinator] Decision failed for {Symbol}", symbol);
            await PublishFallbackDecisionCycleAsync(
                cycleId,
                symbol,
                currentPrice,
                reason: $"decision-exception: {ex.GetType().Name}");
        }
    }

    private async Task PublishFallbackDecisionCycleAsync(string cycleId, string symbol, double currentPrice, string reason)
    {
        try
        {
            _latestTickBySymbol.TryGetValue(symbol, out var tick);
            var change24h = tick?.Change24H ?? 0d;

            // Small, deterministic "always act" policy:
            // - If we have a price, pick a side based on short-term drift (change_24h sign).
            // - If drift is flat, alternate by second parity (deterministic enough, no RNG).
            var direction =
                currentPrice <= 0 ? "HOLD" :
                Math.Abs(change24h) >= 0.15 ? (change24h >= 0 ? "BUY" : "SELL")
                : (DateTime.UtcNow.Second % 2 == 0 ? "BUY" : "SELL");

            var decisionId = $"FALLBACK_{cycleId}";
            var decision = new TradingDecisionEvent
            {
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = direction,
                Confidence = direction == "HOLD" ? 0 : 55,
                SuggestedPositionPct = direction == "HOLD" ? 0 : 1.0, // tiny probe trade
                SuggestedPrice = currentPrice > 0 ? currentPrice : 0,
                SentimentSummary = "fallback: LLM unavailable",
                TechnicalSummary = $"fallback: change24h={change24h:F3}%",
                NewsSummary = "",
                Reasoning = reason,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await PublishAsync(decision);

            await PublishAsync(new DecisionCycleCompletedEvent
            {
                CycleId = cycleId,
                DecisionId = decisionId,
                Symbol = symbol,
                Direction = decision.Direction,
                Confidence = decision.Confidence,
                Executed = decision.Direction != "HOLD",
                ExecutionMode = _executionMode,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Coordinator] Failed to publish fallback decision cycle for {Symbol}", symbol);
            await PublishAsync(new DecisionCycleCompletedEvent
            {
                CycleId = cycleId,
                DecisionId = string.Empty,
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 0,
                Executed = false,
                ExecutionMode = _executionMode,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
    }

    private string BuildDecisionPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Please synthesize the following analysis reports and make a trading decision:");
        sb.AppendLine();

        // Current price snapshot (for fast reaction)
        var symbol =
            _coordState.LatestTechnical?.Symbol
            ?? _coordState.LatestSentiment?.Symbol
            ?? _coordState.LatestNews?.AffectedSymbols.FirstOrDefault()
            ?? "";
        if (!string.IsNullOrWhiteSpace(symbol) && _latestTickBySymbol.TryGetValue(symbol, out var tick) && tick.Price > 0)
        {
            sb.AppendLine("【Market Tick Snapshot】");
            sb.AppendLine($"- Symbol: {tick.Symbol}");
            sb.AppendLine($"- Price: {tick.Price:F2}");
            sb.AppendLine($"- Bid/Ask: {tick.Bid:F2} / {tick.Ask:F2}");
            sb.AppendLine($"- 24h Change: {tick.Change24H:F2}%");
            sb.AppendLine($"- Timestamp(UTC): {tick.Timestamp.ToDateTime():O}");
            sb.AppendLine();
        }

        // Sentiment analysis
        if (_coordState.LatestSentiment != null)
        {
            var s = _coordState.LatestSentiment;
            var fearGreed = double.IsNaN(s.FearGreedIndex) ? "N/A" : s.FearGreedIndex.ToString("F0");
            var longShort = double.IsNaN(s.LongShortRatio) ? "N/A" : s.LongShortRatio.ToString("F2");
            var funding = double.IsNaN(s.FundingRate) ? "N/A" : $"{s.FundingRate * 100:F4}%";
            var openInterest = double.IsNaN(s.OpenInterest) ? "N/A" : s.OpenInterest.ToString("F0");
            sb.AppendLine("【Market Sentiment Analyst Report】");
            sb.AppendLine($"- Sentiment Score: {s.SentimentScore} (-100~+100)");
            sb.AppendLine($"- Sentiment Trend: {s.SentimentTrend}");
            sb.AppendLine($"- Fear & Greed Index: {fearGreed}");
            sb.AppendLine($"- Long/Short Ratio: {longShort}");
            sb.AppendLine($"- Funding Rate: {funding}");
            sb.AppendLine($"- Open Interest: {openInterest}");
            sb.AppendLine($"- Analysis Summary: {s.AnalysisSummary}");
            sb.AppendLine($"- Confidence: {s.Confidence}%");
            sb.AppendLine();
        }

        // Technical analysis
        if (_coordState.LatestTechnical != null)
        {
            var t = _coordState.LatestTechnical;
            sb.AppendLine("【Technical Analyst Report】");
            sb.AppendLine($"- Trend Direction: {t.TrendDirection}");
            sb.AppendLine($"- Trend Strength: {t.TrendStrength}/10");
            sb.AppendLine($"- Trading Signal: {t.Signal}");
            sb.AppendLine($"- RSI: {t.Rsi:F2}");
            sb.AppendLine($"- MACD: {t.Macd:F4}");
            sb.AppendLine($"- Support Level: {t.SupportLevel:F2}");
            sb.AppendLine($"- Resistance Level: {t.ResistanceLevel:F2}");
            if (!string.IsNullOrEmpty(t.PatternDetected))
                sb.AppendLine($"- Pattern Detected: {t.PatternDetected}");
            sb.AppendLine($"- Analysis Summary: {t.AnalysisSummary}");
            sb.AppendLine($"- Confidence: {t.Confidence}%");
            sb.AppendLine();
        }

        // News analysis
        if (_coordState.LatestNews != null)
        {
            var n = _coordState.LatestNews;
            sb.AppendLine("【News Analyst Report】");
            sb.AppendLine($"- Headline: {n.Headline}");
            sb.AppendLine($"- Impact Type: {n.ImpactType}");
            sb.AppendLine($"- Impact Level: {n.ImpactLevel}");
            sb.AppendLine($"- Impact Duration: {n.ImpactDuration}");
            sb.AppendLine($"- Analysis Summary: {n.AnalysisSummary}");
            sb.AppendLine($"- Confidence: {n.Confidence}%");
            sb.AppendLine();
        }

        sb.AppendLine("【Decision Weight Configuration】");
        sb.AppendLine($"- Sentiment Analysis Weight: {_sentimentWeight * 100}%");
        sb.AppendLine($"- Technical Analysis Weight: {_technicalWeight * 100}%");
        sb.AppendLine($"- News Analysis Weight: {_newsWeight * 100}%");
        sb.AppendLine($"- Minimum Trading Confidence: {_minConfidenceToTrade}%");
        sb.AppendLine();
        sb.AppendLine("Return ONLY one JSON object. No markdown, no code fences, no extra text.");
        sb.AppendLine("""
Schema:
{
  "direction": "BUY|SELL|HOLD",
  "confidence": 1-100,
  "position_pct": 0-30,
  "reasoning": {
    "sentiment_factor": "...",
    "technical_factor": "...",
    "news_factor": "...",
    "final_logic": "..."
  }
}
""");

        return sb.ToString();
    }

    // ParseDecisionResponse moved to TradingCoordinatorAgent.Prompt.cs
}
