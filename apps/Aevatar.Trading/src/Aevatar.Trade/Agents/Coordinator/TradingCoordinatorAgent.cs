using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Aevatar.Trade.Agents.Coordinator;

/// <summary>
/// Trading coordinator agent (Chief trading decision maker)
/// Responsibilities: Synthesize opinions from all analysts and make final trading decisions
/// </summary>
public class TradingCoordinatorAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are the chief trading decision maker, responsible for synthesizing analysis from all parties to make final trading decisions.

        【Your Analysis Team】
        1. Market Sentiment Analyst - Provides market sentiment scores and trend judgments
        2. Technical Analyst - Provides technical indicator analysis and trend judgments
        3. News Analyst - Provides news event impact assessments

        【Decision Framework】

        1. Signal Consistency Judgment
           - All three bullish: Strong buy signal
           - All three bearish: Strong sell signal
           - Two bullish, one neutral: Moderate buy
           - Two bearish, one neutral: Moderate sell
           - Analysis divergence: Prefer conservative, wait and see

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

        【Risk Awareness】
        - Choose to wait and see when uncertain
        - Avoid chasing rallies and selling dips
        - Control single position size
        - Leave room to handle unexpected situations
        """;

    // ============ State ============

    private readonly CoordinatorState _coordState = new();
    
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
    private const int DecisionMinIntervalSeconds = 1;
    private int _decisionRunning;
    private DateTime _lastNoStrategyExplainUtc = DateTime.MinValue;
    private const int NoStrategyExplainIntervalSeconds = 60;
    private DateTime _decisionInFlightStartUtc = DateTime.MinValue;
    private DateTime _lastInFlightExplainUtc = DateTime.MinValue;
    private const int InFlightExplainAfterSeconds = 60;
    private const int InFlightExplainIntervalSeconds = 60;

    // Latest market snapshot (from DataCollector) for pricing / order placement.
    private readonly Dictionary<string, MarketTickEvent> _latestTickBySymbol = new(StringComparer.OrdinalIgnoreCase);

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
        return Task.FromResult(
            $"TradingCoordinator: Decisions={_coordState.DecisionsMade}, " +
            $"Approved={_coordState.DecisionsApproved}, " +
            $"Rejected={_coordState.DecisionsRejected}");
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
        int decisionTimeoutSeconds = 300)
    {
        _minConfidenceToTrade = minConfidence;
        _sentimentWeight = sentimentWeight;
        _technicalWeight = technicalWeight;
        _newsWeight = newsWeight;
        _executionMode = executionMode.ToString();
        _decisionTimeoutSeconds = Math.Clamp(decisionTimeoutSeconds, 3, 3600);

        Logger.LogInformation(
            "[Coordinator] Configured: MinConf={MinConf}, Weights=[S:{S}, T:{T}, N:{N}], Timeout={Timeout}s",
            minConfidence, sentimentWeight, technicalWeight, newsWeight, _decisionTimeoutSeconds);
    }

    private void ApplyPolicy(TradingPolicyConfig policy)
    {
        if (policy.Trading != null)
        {
            _minConfidenceToTrade = policy.Trading.MinConfidenceToTrade;
            _executionMode = policy.Trading.ExecutionMode.ToString();
        }

        if (policy.Analysis != null)
        {
            _sentimentWeight = policy.Analysis.SentimentWeight;
            _technicalWeight = policy.Analysis.TechnicalWeight;
            _newsWeight = policy.Analysis.NewsWeight;
        }
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

        // NOTE:
        // - 这里只更新价格快照，不触发决策。
        // - 决策入口由 DecisionTriggerEvent 控制。
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle market sentiment analysis results
    /// </summary>
    [EventHandler]
    public async Task HandleSentimentAnalysis(MarketSentimentAnalysisEvent evt)
    {
        _coordState.LatestSentiment = evt;
        Logger.LogDebug(
            "[Coordinator] Received sentiment: Score={Score}, Trend={Trend}",
            evt.SentimentScore, evt.SentimentTrend);

        // Broadcast downstream so UI/streaming agents can consume analysis snapshots.
        await PublishAsync(evt, Aevatar.Agents.EventDirection.Down);
    }

    /// <summary>
    /// Handle technical analysis results
    /// </summary>
    [EventHandler]
    public async Task HandleTechnicalAnalysis(TechnicalAnalysisEvent evt)
    {
        _coordState.LatestTechnical = evt;
        Logger.LogDebug(
            "[Coordinator] Received technical: Trend={Trend}, Signal={Signal}",
            evt.TrendDirection, evt.Signal);

        // Broadcast downstream so UI/streaming agents can consume analysis snapshots.
        await PublishAsync(evt, Aevatar.Agents.EventDirection.Down);
    }

    /// <summary>
    /// Handle news impact analysis results
    /// </summary>
    [EventHandler]
    public async Task HandleNewsAnalysis(NewsImpactAnalysisEvent evt)
    {
        _coordState.LatestNews = evt;
        Logger.LogDebug(
            "[Coordinator] Received news: Impact={Impact}, Level={Level}",
            evt.ImpactType, evt.ImpactLevel);

        // Broadcast downstream so UI/streaming agents can consume analysis snapshots.
        await PublishAsync(evt, Aevatar.Agents.EventDirection.Down);
    }

    /// <summary>
    /// Handle decision triggers (price-change driven)
    /// </summary>
    [EventHandler]
    public Task HandleDecisionTrigger(DecisionTriggerEvent evt)
    {
        _ = TryMakeDecisionAsync(evt);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle policy updates (weights/confidence)
    /// </summary>
    [EventHandler]
    public async Task HandleTradingPolicyUpdated(TradingPolicyUpdatedEvent evt)
    {
        if (evt.Policy != null)
        {
            ApplyPolicy(evt.Policy);
            Logger.LogInformation("[Coordinator] Policy updated by {By}", evt.UpdatedBy);
        }

        await PublishAsync(evt, Aevatar.Agents.EventDirection.Down);
    }

    /// <summary>
    /// Handle trade approval events (for statistics)
    /// </summary>
    [EventHandler]
    public Task HandleTradeApproved(ApprovedTradeEvent evt)
    {
        _coordState.DecisionsApproved++;
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
        _coordState.DecisionsRejected++;
        Logger.LogWarning(
            "[Coordinator] Decision rejected: {DecisionId}, Reason: {Reason}",
            evt.DecisionId, evt.RejectionReason);
        return Task.CompletedTask;
    }

    // ============ Decision Logic ============

    /// <summary>
    /// Attempt to make a trading decision
    /// </summary>
    private async Task TryMakeDecisionAsync(DecisionTriggerEvent? trigger)
    {
        // Check if there is sufficient analysis data.
        // If not, emit a periodic "why no strategy" cycle so the user always sees a reason in trade-audit (*.md).
        if (!HasSufficientData(trigger?.Symbol, out var noDataReason))
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNoStrategyExplainUtc).TotalSeconds >= NoStrategyExplainIntervalSeconds)
            {
                _lastNoStrategyExplainUtc = now;
                var triggerLabel = BuildTriggerLabel(trigger);
                await PublishNoStrategyCycleAsync($"{triggerLabel}: {noDataReason}");
            }

            Logger.LogDebug("[Coordinator] Insufficient data for decision: {Reason}", noDataReason);
            return;
        }

        // If a decision is already running (LLM call in-flight), do NOT start another one.
        // Instead, periodically emit a "waiting" reason so the log keeps moving and users
        // don't feel the system is deadlocked.
        if (Volatile.Read(ref _decisionRunning) == 1)
        {
            var now = DateTime.UtcNow;
            var startedAt = _decisionInFlightStartUtc == DateTime.MinValue ? now : _decisionInFlightStartUtc;
            var elapsed = now - startedAt;

            if (elapsed.TotalSeconds >= InFlightExplainAfterSeconds &&
                (now - _lastInFlightExplainUtc).TotalSeconds >= InFlightExplainIntervalSeconds)
            {
                _lastInFlightExplainUtc = now;
                await PublishNoStrategyCycleAsync($"waiting for LLM response (in-flight {elapsed.TotalSeconds:F0}s)");
            }

            return;
        }

        // Check decision interval (to avoid being too frequent)
        if (_coordState.LastDecisionTime != null)
        {
            var elapsed = DateTime.UtcNow - _coordState.LastDecisionTime.ToDateTime();
            if (elapsed.TotalSeconds < DecisionMinIntervalSeconds)
            {
                Logger.LogDebug("[Coordinator] Decision throttled, last decision {Seconds}s ago",
                    elapsed.TotalSeconds);
                return;
            }
        }

        // Re-entrancy guard: do not run multiple LLM calls concurrently.
        if (Interlocked.Exchange(ref _decisionRunning, 1) == 1)
            return;

        try
        {
            _decisionInFlightStartUtc = DateTime.UtcNow;
            await MakeDecisionAsync(trigger);
        }
        finally
        {
            Interlocked.Exchange(ref _decisionRunning, 0);
            _decisionInFlightStartUtc = DateTime.MinValue;
        }
    }

    private bool HasSufficientData() => HasSufficientData(null, out _);

    private bool HasSufficientData(string? preferredSymbol, out string reason)
    {
        reason = "";

        // ------------------------------------------------------------
        //  高频模式：不要用“5分钟新鲜度”把系统锁死
        //
        //  现实情况：
        //  - 15m/5m K 线：技术分析天然更新慢
        //  - 只要有 tick（价格）+ 至少一个分析维度，就允许决策
        // ------------------------------------------------------------

        var hasAnyAnalysis = _coordState.LatestSentiment != null || _coordState.LatestTechnical != null || _coordState.LatestNews != null;
        if (!hasAnyAnalysis)
        {
            reason = "no analysis yet (Sentiment/Technical/News all missing) — likely LLM not configured or analysis not triggered";
            return false;
        }

        var symbol = ResolveSymbol(preferredSymbol);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            reason = "no symbol resolved from analysis";
            return false;
        }

        if (!_latestTickBySymbol.TryGetValue(symbol, out var tick) || tick.Price <= 0)
        {
            reason = $"no market tick snapshot for {symbol} (ticker polling may be failing)";
            return false;
        }

        return true;
    }

    private string ResolveSymbol(string? preferredSymbol)
    {
        if (!string.IsNullOrWhiteSpace(preferredSymbol))
            return preferredSymbol.Trim();

        return _coordState.LatestTechnical?.Symbol
               ?? _coordState.LatestSentiment?.Symbol
               ?? _coordState.LatestNews?.AffectedSymbols.FirstOrDefault()
               ?? "";
    }

    private async Task PublishNoStrategyCycleAsync(string reason)
    {
        try
        {
            // Best-effort: pick a symbol to show in the log
            var symbol =
                _coordState.LatestTechnical?.Symbol
                ?? _coordState.LatestSentiment?.Symbol
                ?? _coordState.LatestNews?.AffectedSymbols.FirstOrDefault()
                ?? _latestTickBySymbol.Keys.FirstOrDefault()
                ?? "UNKNOWN";

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

    private static string TrimReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "";
        var s = reason.Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length > 180)
            s = s[..180] + "…";
        return s;
    }

    private static string BuildTriggerLabel(DecisionTriggerEvent? trigger)
    {
        if (trigger == null)
            return "MANUAL";

        var reason = string.IsNullOrWhiteSpace(trigger.Reason) ? "TRIGGER" : trigger.Reason.Trim();
        var symbol = string.IsNullOrWhiteSpace(trigger.Symbol) ? "" : trigger.Symbol.Trim();
        var delta = Math.Abs(trigger.DeltaPct) > 0.0001 ? $" Δ%={trigger.DeltaPct:F4}" : "";

        if (symbol.Length == 0)
            return $"{reason}{delta}";

        return $"{reason}:{symbol}{delta}";
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

    private async Task MakeDecisionAsync(DecisionTriggerEvent? trigger)
    {
        var symbol = ResolveSymbol(trigger?.Symbol);
        var prompt = BuildDecisionPrompt();
        var cycleId = Guid.NewGuid().ToString("N")[..16];

        // Pricing snapshot (best-effort). This will be attached to the decision so downstream agents can size orders.
        _latestTickBySymbol.TryGetValue(symbol, out var tick);
        var currentPrice = tick?.Price ?? 0d;

        await PublishAsync(new DecisionCycleStartedEvent
        {
            CycleId = cycleId,
            Symbol = symbol,
            Trigger = BuildTriggerLabel(trigger),
            CoordinatorId = _coordState.AgentId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        try
        {
            var engine = DecisionEngine ?? new DirectLlmDecisionEngine(async (p, ct) =>
            {
                var chat = await ChatAsync(ChatRequest.Create(p), ct);
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
            _coordState.LastDecision = decision.Direction;
            _coordState.DecisionsMade++;
            _coordState.LastDecisionTime = Timestamp.FromDateTime(DateTime.UtcNow);

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
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Coordinator] Decision failed for {Symbol}", symbol);
            
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

    private TradingDecisionEvent ParseDecisionResponse(string response, string symbol)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            var reasoning = "";
            if (root.TryGetProperty("reasoning", out var reasoningObj))
            {
                var parts = new List<string>();
                if (reasoningObj.TryGetProperty("sentiment_factor", out var sf))
                    parts.Add($"Sentiment: {sf.GetString()}");
                if (reasoningObj.TryGetProperty("technical_factor", out var tf))
                    parts.Add($"Technical: {tf.GetString()}");
                if (reasoningObj.TryGetProperty("news_factor", out var nf))
                    parts.Add($"News: {nf.GetString()}");
                if (reasoningObj.TryGetProperty("final_logic", out var fl))
                    parts.Add($"Decision Logic: {fl.GetString()}");
                reasoning = string.Join(" | ", parts);
            }

            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = root.TryGetProperty("direction", out var dir)
                    ? dir.GetString() ?? "HOLD" : "HOLD",
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetInt32() : 50,
                SuggestedPositionPct = root.TryGetProperty("position_pct", out var pos)
                    ? pos.GetDouble() : 10,
                SentimentSummary = _coordState.LatestSentiment?.AnalysisSummary ?? "",
                TechnicalSummary = _coordState.LatestTechnical?.AnalysisSummary ?? "",
                NewsSummary = _coordState.LatestNews?.AnalysisSummary ?? "",
                Reasoning = reasoning,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 30,
                Reasoning = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }
}
