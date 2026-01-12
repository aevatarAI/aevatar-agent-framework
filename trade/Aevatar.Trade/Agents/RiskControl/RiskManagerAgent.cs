using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Trade.Tools;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.RiskControl;

/// <summary>
/// Risk manager agent
/// Responsibilities: Assess trading risks, set stop loss and take profit, reject trades when necessary
/// </summary>
public class RiskManagerAgent : AIGAgentBase
{
    // Risk LLM must not block the stream forever; otherwise Coordinator publishes get back-pressured.
    private const int LlmTimeoutSeconds = 300;

    // ============================================================
    //  高频行情事件“止血阀”
    //
    //  背景（本质）：
    //  - LocalMessageStream 是有界队列（满了会 Wait），会产生反压。
    //  - MarketTick/Kline 属于高频“背景噪声”，Risk/Executor/Audit 并不需要处理它们。
    //  - 但如果不拦截，Tick 会沿层级一路 DOWN 到 Executor/Audit，导致队列堆积，
    //    最终把上游 Publish 卡住，表现为“系统停了、日志停了、DecisionCycle 不闭环”。
    //
    //  方案：
    //  - 在 RiskManager 作为“决策/执行链路的闸门”，对 MarketTick/Kline 事件把 Direction 改为 Unspecified，
    //    让 Actor 层的 ContinuePropagationAsync 不再向下传播（不会再打到 Executor/Audit）。
    // ============================================================
    [AllEventHandler(Priority = 0)]
    private Task CutOffMarketDataPropagation(EventEnvelope envelope)
    {
        if (envelope.Direction != EventDirection.Down)
            return Task.CompletedTask;

        var typeUrl = envelope.Payload?.TypeUrl ?? "";
        if (typeUrl.EndsWith("MarketTickEvent", StringComparison.Ordinal) ||
            typeUrl.EndsWith("KlineUpdateEvent", StringComparison.Ordinal))
        {
            envelope.Direction = EventDirection.Unspecified;
        }

        return Task.CompletedTask;
    }
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are a risk manager responsible for protecting capital safety, the last line of defense for trading decisions.

        【Risk Control Rules - Hard Constraints】

        1. Single Trade Risk Control
           - Maximum loss per trade: Not exceeding 2% of total capital
           - Maximum position per trade: Not exceeding 10% of total capital

        2. Overall Risk Control
           - Total position limit: Not exceeding 30% of total capital
           - Maximum daily loss: Not exceeding 5% of total capital
           - Consecutive loss circuit breaker: Pause for 1 hour after 3 consecutive losses

        3. Stop Loss and Take Profit Settings
           - Stop loss: 1-3% (dynamically adjusted based on ATR)
           - Take profit: 1.5-3x stop loss (risk-reward ratio)

        【Risk Assessment Dimensions】

        1. Market Risk
           - Current volatility (ATR)
           - Liquidity conditions
           - Extreme market condition risk

        2. Position Risk
           - Current position ratio
           - Unrealized P&L
           - Risk exposure concentration

        3. Trading Risk
           - Entry timing
           - Slippage risk
           - Execution risk

        【Output Format】
        Please strictly output in the following JSON format:
        {
            "approved": <true|false>,
            "risk_level": "<LOW|MEDIUM|HIGH|EXTREME>",
            "adjusted_position_pct": <adjusted position percentage>,
            "stop_loss_pct": <stop loss percentage>,
            "take_profit_pct": <take profit percentage>,
            "violated_rules": ["list of violated rules"],
            "risk_notes": "<risk assessment notes>",
            "summary": "<one-sentence risk control conclusion>"
        }

        【Rejection Conditions - Any trigger results in rejection】
        - Exceeding daily loss limit
        - Consecutive losses without cooldown
        - Position limit exceeded
        - Extreme market conditions
        - Confidence too low
        """;

    // ============ State ============

    private readonly RiskManagerState _riskState = new();

    // Risk control configuration
    private double _maxPositionPct = 10.0;      // Maximum position per trade 10%
    private double _maxTotalPositionPct = 30.0; // Total position limit 30%
    private double _maxLossPerTrade = 2.0;      // Maximum loss per trade 2%
    private double _maxDailyLossPct = 5.0;      // Maximum daily loss 5%
    private int _maxConsecutiveLosses = 3;      // Consecutive loss circuit breaker
    private int _cooldownMinutes = 60;          // Circuit breaker cooldown time
    private int _minConfidenceToTrade = 60;     // Coordinator gating (avoid accidental trading on low-confidence decisions)

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        _riskState.AgentId = Id.ToString();
        _riskState.IsTradingAllowed = true;
        _riskState.CircuitBreakerStatus = "NORMAL";
        
        Logger.LogInformation("[RiskManager] Activated: {AgentId}", _riskState.AgentId);
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        // Read-only tools: always safe to expose (IsDangerous=false in manifest)
        await RegisterDotNetFileSkillAsync(TradeDotNetSkillPaths.WeexGetBalances, cancellationToken);
        await RegisterDotNetFileSkillAsync(TradeDotNetSkillPaths.WeexGetOrder, cancellationToken);
        await RegisterDotNetFileSkillAsync(TradeDotNetSkillPaths.WeexGetOpenOrders, cancellationToken);

        // Trading tools (dangerous): only visible/executable when AllowDangerousTools=true
        await RegisterDotNetFileSkillAsync(TradeDotNetSkillPaths.WeexPlaceOrder, cancellationToken);
        await RegisterDotNetFileSkillAsync(TradeDotNetSkillPaths.WeexCancelOrder, cancellationToken);

        // AI Wars (WEEX Alpha Awakens): register every endpoint as a dotnet-file skill.
        // Safety is enforced by the tool system using the per-skill manifest flags (IsDangerous/RequiresConfirmation)
        // + this agent's AllowDangerousTools switch.
        foreach (var skillPath in TradeDotNetSkillPaths.WeexAiWarsAll)
        {
            await RegisterDotNetFileSkillAsync(skillPath, cancellationToken);
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"RiskManager: Position={_riskState.PositionRatio:F1}%, " +
            $"DailyPnL={_riskState.DailyPnlPct:F2}%, " +
            $"Approved={_riskState.TradesApproved}, " +
            $"Rejected={_riskState.TradesRejected}");
    }

    /// <summary>
    /// Configure risk control parameters
    /// </summary>
    public void Configure(
        double maxPositionPct = 10.0,
        double maxTotalPositionPct = 30.0,
        double maxLossPerTrade = 2.0,
        double maxDailyLossPct = 5.0,
        int maxConsecutiveLosses = 3,
        int cooldownMinutes = 60,
        int minConfidenceToTrade = 60)
    {
        _maxPositionPct = maxPositionPct;
        _maxTotalPositionPct = maxTotalPositionPct;
        _maxLossPerTrade = maxLossPerTrade;
        _maxDailyLossPct = maxDailyLossPct;
        _maxConsecutiveLosses = maxConsecutiveLosses;
        _cooldownMinutes = cooldownMinutes;
        _minConfidenceToTrade = minConfidenceToTrade;

        Logger.LogInformation(
            "[RiskManager] Configured: MaxPos={MaxPos}%, MaxTotal={MaxTotal}%, " +
            "MaxLoss={MaxLoss}%, MaxDaily={MaxDaily}%, MinConf={MinConf}%",
            maxPositionPct, maxTotalPositionPct, maxLossPerTrade, maxDailyLossPct, minConfidenceToTrade);
    }

    /// <summary>
    /// Update account information
    /// </summary>
    public void UpdateAccountInfo(
        double totalEquity,
        double availableBalance,
        double currentPositionValue,
        double unrealizedPnl)
    {
        _riskState.TotalEquity = totalEquity;
        _riskState.AvailableBalance = availableBalance;
        _riskState.CurrentPositionValue = currentPositionValue;
        _riskState.UnrealizedPnl = unrealizedPnl;
        _riskState.PositionRatio = totalEquity > 0 
            ? (currentPositionValue / totalEquity) * 100 
            : 0;
    }

    // ============ Event Handlers ============

    /// <summary>
    /// Handle trading decisions and perform risk assessment
    /// </summary>
    [EventHandler]
    public async Task HandleTradingDecision(TradingDecisionEvent evt)
    {
        Logger.LogInformation(
            "[RiskManager] Evaluating decision: {DecisionId}, {Direction} {Symbol}",
            evt.DecisionId, evt.Direction, evt.Symbol);

        // ------------------------------------------------------------
        //  "No trade" fast path
        //
        //  We intentionally emit a TradeRejectedEvent so TradeAudit can
        //  render a human-readable reason in trade-audit (*.md).
        // ------------------------------------------------------------
        if (string.Equals(evt.Direction, "HOLD", StringComparison.OrdinalIgnoreCase))
        {
            await RejectTrade(evt, new List<string> { "No trade: Coordinator decision is HOLD" }, "LOW");
            return;
        }

        if (evt.Confidence < _minConfidenceToTrade)
        {
            await RejectTrade(
                evt,
                new List<string> { $"No trade: confidence {evt.Confidence} < min {_minConfidenceToTrade}" },
                "LOW");
            return;
        }

        // First perform hard rule checks
        var hardCheckResult = PerformHardRuleCheck(evt);
        if (!hardCheckResult.Passed)
        {
            await RejectTrade(evt, hardCheckResult.ViolatedRules, "EXTREME");
            return;
        }

        // AI-assisted risk assessment
        await EvaluateWithAIAsync(evt);
    }

    /// <summary>
    /// Handle order execution results
    /// </summary>
    [EventHandler]
    public Task HandleOrderExecuted(OrderExecutedEvent evt)
    {
        // Update P&L statistics
        // Simplified handling here, should actually track P&L for each trade
        Logger.LogDebug("[RiskManager] Order executed: {OrderId}", evt.OrderId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle order failures
    /// </summary>
    [EventHandler]
    public Task HandleOrderFailed(OrderFailedEvent evt)
    {
        Logger.LogWarning(
            "[RiskManager] Order failed: {ClientOrderId}, Error: {Error}",
            evt.ClientOrderId, evt.ErrorMessage);
        return Task.CompletedTask;
    }

    // ============ Risk Evaluation ============

    private (bool Passed, List<string> ViolatedRules) PerformHardRuleCheck(TradingDecisionEvent evt)
    {
        var violations = new List<string>();

        // 1. Check circuit breaker status
        if (!_riskState.IsTradingAllowed)
        {
            violations.Add($"Trading circuit breaker active, status: {_riskState.CircuitBreakerStatus}");
        }

        // 2. Check daily loss limit
        if (_riskState.DailyPnlPct <= -_maxDailyLossPct)
        {
            violations.Add($"Daily loss limit reached: {_riskState.DailyPnlPct:F2}% >= {_maxDailyLossPct}%");
        }

        // 3. Check consecutive losses
        if (_riskState.ConsecutiveLosses >= _maxConsecutiveLosses)
        {
            violations.Add($"Consecutive losses: {_riskState.ConsecutiveLosses} >= {_maxConsecutiveLosses}");
        }

        // 4. Check total position limit
        var newPositionRatio = _riskState.PositionRatio + evt.SuggestedPositionPct;
        if (newPositionRatio > _maxTotalPositionPct)
        {
            violations.Add($"Total position will exceed limit: {newPositionRatio:F1}% > {_maxTotalPositionPct}%");
        }

        // 5. Check single trade position limit
        if (evt.SuggestedPositionPct > _maxPositionPct)
        {
            violations.Add($"Single trade position exceeds limit: {evt.SuggestedPositionPct:F1}% > {_maxPositionPct}%");
        }

        return (violations.Count == 0, violations);
    }

    private async Task EvaluateWithAIAsync(TradingDecisionEvent evt)
    {
        var prompt = BuildEvaluationPrompt(evt);

        try
        {
            var timeout = TimeSpan.FromSeconds(LlmTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);

            ChatResponse chat;
            try
            {
                var task = ChatAsync(ChatRequest.Create(prompt), timeoutCts.Token);
                chat = await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                timeoutCts.Cancel();
                Logger.LogWarning(
                    "[RiskManager] LLM timeout >{Timeout}s. Rejecting trade to keep system unblocked. DecisionId={DecisionId}",
                    LlmTimeoutSeconds, evt.DecisionId);
                await RejectTrade(evt, new List<string> { $"risk-llm-timeout>{LlmTimeoutSeconds}s" }, "HIGH");
                return;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Logger.LogWarning(
                    "[RiskManager] LLM timeout >{Timeout}s. Rejecting trade to keep system unblocked. DecisionId={DecisionId}",
                    LlmTimeoutSeconds, evt.DecisionId);
                await RejectTrade(evt, new List<string> { $"risk-llm-timeout>{LlmTimeoutSeconds}s" }, "HIGH");
                return;
            }

            var evaluation = ParseEvaluationResponse(chat.Content ?? string.Empty);

            if (evaluation.Approved)
            {
                await ApproveTrade(evt, evaluation);
            }
            else
            {
                await RejectTrade(evt, evaluation.ViolatedRules, evaluation.RiskLevel);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[RiskManager] AI evaluation failed, rejecting trade");
            await RejectTrade(evt, new List<string> { "Risk control AI evaluation failed" }, "HIGH");
        }
    }

    private string BuildEvaluationPrompt(TradingDecisionEvent evt)
    {
        return $"""
            Please assess the risk of the following trading decision:

            【Trading Decision】
            - Decision ID: {evt.DecisionId}
            - Trading Pair: {evt.Symbol}
            - Direction: {evt.Direction}
            - Suggested Position: {evt.SuggestedPositionPct}%
            - Decision Confidence: {evt.Confidence}%
            - Decision Reasoning: {evt.Reasoning}

            【Current Account Status】
            - Total Equity: ${_riskState.TotalEquity:F2}
            - Available Balance: ${_riskState.AvailableBalance:F2}
            - Current Position: {_riskState.PositionRatio:F1}%
            - Daily P&L: {_riskState.DailyPnlPct:F2}%
            - Unrealized P&L: ${_riskState.UnrealizedPnl:F2}
            - Consecutive Losses: {_riskState.ConsecutiveLosses}
            - Consecutive Wins: {_riskState.ConsecutiveWins}

            【Risk Control Configuration】
            - Maximum Position Per Trade: {_maxPositionPct}%
            - Total Position Limit: {_maxTotalPositionPct}%
            - Maximum Loss Per Trade: {_maxLossPerTrade}%
            - Maximum Daily Loss: {_maxDailyLossPct}%

            【Analysis Summary】
            - Sentiment: {evt.SentimentSummary}
            - Technical: {evt.TechnicalSummary}
            - News: {evt.NewsSummary}
            """;
    }

    private RiskEvaluation ParseEvaluationResponse(string response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            var violations = new List<string>();
            if (root.TryGetProperty("violated_rules", out var rulesArray))
            {
                foreach (var rule in rulesArray.EnumerateArray())
                {
                    violations.Add(rule.GetString() ?? "");
                }
            }

            return new RiskEvaluation
            {
                Approved = root.TryGetProperty("approved", out var approved) && approved.GetBoolean(),
                RiskLevel = root.TryGetProperty("risk_level", out var level) 
                    ? level.GetString() ?? "MEDIUM" : "MEDIUM",
                AdjustedPositionPct = root.TryGetProperty("adjusted_position_pct", out var pos) 
                    ? pos.GetDouble() : 10,
                StopLossPct = root.TryGetProperty("stop_loss_pct", out var sl) 
                    ? sl.GetDouble() : 2,
                TakeProfitPct = root.TryGetProperty("take_profit_pct", out var tp) 
                    ? tp.GetDouble() : 4,
                ViolatedRules = violations,
                RiskNotes = root.TryGetProperty("risk_notes", out var notes) 
                    ? notes.GetString() ?? "" : "",
                Summary = root.TryGetProperty("summary", out var summary) 
                    ? summary.GetString() ?? "" : ""
            };
        }
        catch
        {
            return new RiskEvaluation
            {
                Approved = false,
                RiskLevel = "HIGH",
                ViolatedRules = new List<string> { "Risk control response parsing failed" },
                RiskNotes = response
            };
        }
    }

    private async Task ApproveTrade(TradingDecisionEvent decision, RiskEvaluation evaluation)
    {
        _riskState.TradesApproved++;
        _riskState.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);

        // ------------------------------------------------------------
        //  Price snapshot (required)
        //
        //  - Coordinator should attach SuggestedPrice from MarketTickEvent.
        //  - RiskManager must NOT guess size without a price; reject if missing.
        // ------------------------------------------------------------
        var currentPrice = decision.SuggestedPrice > 0 ? decision.SuggestedPrice : 0;
        if (currentPrice <= 0)
        {
            await RejectTrade(
                decision,
                new List<string> { "Missing current price (TradingDecisionEvent.suggested_price <= 0)" },
                riskLevel: "HIGH");
            return;
        }

        if (_riskState.TotalEquity <= 0)
        {
            await RejectTrade(
                decision,
                new List<string> { "Account equity is unknown (sync-account not completed yet)" },
                riskLevel: "HIGH");
            return;
        }

        // Calculate actual stop loss and take profit prices
        var stopLoss = decision.Direction == "BUY" 
            ? currentPrice * (1 - evaluation.StopLossPct / 100)
            : currentPrice * (1 + evaluation.StopLossPct / 100);
        var takeProfit = decision.Direction == "BUY"
            ? currentPrice * (1 + evaluation.TakeProfitPct / 100)
            : currentPrice * (1 - evaluation.TakeProfitPct / 100);

        // ------------------------------------------------------------
        //  Order policy (AI Wars demo friendly)
        //  - Prefer LIMIT at current price so it becomes a real "挂单" workflow.
        // ------------------------------------------------------------
        var orderType = "limit";
        var quantity = CalculateQuantity(decision.Symbol, evaluation.AdjustedPositionPct, currentPrice);
        if (quantity <= 0)
        {
            await RejectTrade(
                decision,
                new List<string> { "Calculated order quantity <= 0 (check equity/positionPct/price)" },
                riskLevel: "HIGH");
            return;
        }

        var approved = new ApprovedTradeEvent
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            Side = decision.Direction.ToLower(),
            OrderType = orderType,
            Quantity = quantity,
            Price = currentPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            RiskAssessment = evaluation.RiskLevel,
            PositionSizeAdjusted = evaluation.AdjustedPositionPct,
            RiskNotes = evaluation.Summary,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(approved);

        Logger.LogInformation(
            "[RiskManager] Trade APPROVED: {DecisionId}, Risk={Risk}, Position={Pos}%",
            decision.DecisionId, evaluation.RiskLevel, evaluation.AdjustedPositionPct);
    }

    private async Task RejectTrade(
        TradingDecisionEvent decision, 
        List<string> violations, 
        string riskLevel)
    {
        _riskState.TradesRejected++;
        _riskState.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);

        var rejected = new TradeRejectedEvent
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            OriginalDirection = decision.Direction,
            RejectionReason = string.Join("; ", violations),
            RiskLevel = riskLevel,
            ViolatedRules = { violations },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(rejected);

        Logger.LogWarning(
            "[RiskManager] Trade REJECTED: {DecisionId}, Reasons: {Reasons}",
            decision.DecisionId, string.Join(", ", violations));
    }

    private double CalculateQuantity(string symbol, double positionPct, double currentPrice)
    {
        // ------------------------------------------------------------
        //  Position sizing (contract "size" uses base asset quantity)
        //
        //  Example:
        //  - equity=1000 USDT, positionPct=10%, price=100000 => qty=0.001 BTC
        //
        //  NOTE:
        //  - StepSize rounding is handled by WeexContractApiClient before placing the order.
        // ------------------------------------------------------------
        if (_riskState.TotalEquity <= 0) return 0;
        if (positionPct <= 0) return 0;
        if (currentPrice <= 0) return 0;

        var positionValueUsdt = _riskState.TotalEquity * (positionPct / 100);
        if (positionValueUsdt <= 0) return 0;

        var qty = positionValueUsdt / currentPrice;
        if (qty <= 0) return 0;

        return Math.Round(qty, 8, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Trigger circuit breaker
    /// </summary>
    public async Task TriggerCircuitBreakerAsync(string reason)
    {
        _riskState.IsTradingAllowed = false;
        _riskState.CircuitBreakerStatus = reason;

        await PublishAsync(new CircuitBreakerTriggeredEvent
        {
            TriggerType = reason,
            Description = $"Risk control circuit breaker triggered: {reason}",
            CooldownMinutes = _cooldownMinutes,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        Logger.LogWarning("[RiskManager] Circuit breaker triggered: {Reason}", reason);

        // Set cooldown timer
        _ = Task.Delay(TimeSpan.FromMinutes(_cooldownMinutes)).ContinueWith(_ =>
        {
            _riskState.IsTradingAllowed = true;
            _riskState.CircuitBreakerStatus = "NORMAL";
            _riskState.ConsecutiveLosses = 0;
            Logger.LogInformation("[RiskManager] Circuit breaker reset");
        });
    }

    // ============ Helper Types ============

    private record RiskEvaluation
    {
        public bool Approved { get; init; }
        public string RiskLevel { get; init; } = "MEDIUM";
        public double AdjustedPositionPct { get; init; }
        public double StopLossPct { get; init; }
        public double TakeProfitPct { get; init; }
        public List<string> ViolatedRules { get; init; } = new();
        public string RiskNotes { get; init; } = "";
        public string Summary { get; init; } = "";
    }
}
