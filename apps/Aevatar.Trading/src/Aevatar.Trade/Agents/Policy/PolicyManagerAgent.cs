using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Policy;

// ============================================================================
//  PolicyManagerAgent
//  - 负责策略参数的集中存储、校验与广播
//  - 提供 AI 工具入口，允许动态调整触发/风控/权重
// ============================================================================

public sealed class PolicyManagerAgent : AIGAgentBase<PolicyManagerState>
{
    public override string SystemPrompt { get; set; } = """
        You are a trading policy manager. Your job is to keep system parameters safe, consistent, and auditable.
        You must validate every update and reject unsafe settings.
        """;

    private TradingPolicyConfig? _bootstrapPolicy;

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        CustomState.AgentId = Id.ToString();
        EnsurePolicyInitialized();

        Logger.LogInformation(
            "[PolicyManager] Activated: {AgentId}, Updates={Count}",
            CustomState.AgentId, CustomState.UpdateCount);
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        await RegisterToolAsync(
            new GetTradingPolicyTool(GetPolicySnapshot),
            cancellationToken: cancellationToken);

        await RegisterToolAsync(
            new UpdateTradingPolicyTool(GetPolicySnapshot, UpdatePolicyAsync),
            cancellationToken: cancellationToken);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"PolicyManager: Updates={CustomState.UpdateCount}, LastBy={CustomState.LastUpdatedBy}");
    }

    // ============ Configuration ============

    public void Configure(TradingPolicyConfig policy)
    {
        _bootstrapPolicy = policy ?? new TradingPolicyConfig();
    }

    public TradingPolicyConfig GetPolicySnapshot()
    {
        var policy = CustomState.Policy ?? new TradingPolicyConfig();
        return NormalizePolicy(policy).Clone();
    }

    public async Task<TradingPolicyUpdatedEvent> UpdatePolicyAsync(
        TradingPolicyConfig policy,
        string? updatedBy,
        string? reason,
        CancellationToken ct = default)
    {
        var normalized = NormalizePolicy(policy);
        var errors = ValidatePolicy(normalized);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid policy: {string.Join("; ", errors)}");
        }

        var ts = Timestamp.FromDateTime(DateTime.UtcNow);
        var by = string.IsNullOrWhiteSpace(updatedBy) ? Id.ToString() : updatedBy.Trim();
        ApplyPolicy(normalized, by, ts);

        var evt = new TradingPolicyUpdatedEvent
        {
            UpdatedBy = by,
            Reason = reason ?? "",
            Policy = normalized.Clone(),
            Timestamp = ts
        };

        await PublishAsync(evt, Aevatar.Agents.EventDirection.Both, ct);
        return evt;
    }

    // ============ Event Handlers ============

    [EventHandler]
    public Task HandleTradingPolicyUpdated(TradingPolicyUpdatedEvent evt)
    {
        if (evt.Policy == null)
            return Task.CompletedTask;

        if (CustomState.Policy != null &&
            evt.UpdatedBy == CustomState.LastUpdatedBy &&
            evt.Policy.ToByteString().Equals(CustomState.Policy.ToByteString()))
        {
            return Task.CompletedTask;
        }

        ApplyPolicy(evt.Policy, evt.UpdatedBy, evt.Timestamp);
        return Task.CompletedTask;
    }

    // ============ Internal ============

    private void EnsurePolicyInitialized()
    {
        if (CustomState.Policy != null)
            return;

        var initial = _bootstrapPolicy ?? new TradingPolicyConfig();
        ApplyPolicy(NormalizePolicy(initial), "SYSTEM_BOOTSTRAP", Timestamp.FromDateTime(DateTime.UtcNow));
    }

    private void ApplyPolicy(TradingPolicyConfig policy, string updatedBy, Timestamp ts)
    {
        CustomState.Policy = policy;
        CustomState.UpdateCount += 1;
        CustomState.LastUpdatedBy = updatedBy ?? "";
        CustomState.LastUpdatedTime = ts;

        Logger.LogInformation(
            "[PolicyManager] Policy updated by {By} (count={Count})",
            updatedBy, CustomState.UpdateCount);
    }

    private static TradingPolicyConfig NormalizePolicy(TradingPolicyConfig policy)
    {
        var normalized = policy.Clone();

        if (normalized.Trading == null)
            normalized.Trading = new TradingConfig();
        if (normalized.Risk == null)
            normalized.Risk = new RiskControlConfig();
        if (normalized.Trigger == null)
            normalized.Trigger = new DecisionTriggerConfig();
        if (normalized.Analysis == null)
            normalized.Analysis = new AnalysisWeightConfig();

        return normalized;
    }

    private static List<string> ValidatePolicy(TradingPolicyConfig policy)
    {
        var errors = new List<string>();

        var trading = policy.Trading;
        if (trading != null)
        {
            if (trading.MinConfidenceToTrade < 0 || trading.MinConfidenceToTrade > 100)
                errors.Add("Trading.MinConfidenceToTrade must be 0-100");
            if (trading.MaxPositionPct < 0 || trading.MaxPositionPct > 100)
                errors.Add("Trading.MaxPositionPct must be 0-100");
            if (trading.MaxTotalPositionPct < 0 || trading.MaxTotalPositionPct > 100)
                errors.Add("Trading.MaxTotalPositionPct must be 0-100");
            if (trading.MaxLossPerTrade < 0 || trading.MaxLossPerTrade > 100)
                errors.Add("Trading.MaxLossPerTrade must be 0-100");
            if (trading.MaxDailyLoss < 0 || trading.MaxDailyLoss > 100)
                errors.Add("Trading.MaxDailyLoss must be 0-100");
        }

        var trigger = policy.Trigger;
        if (trigger != null)
        {
            if (trigger.PriceChangePct < 0 || trigger.PriceChangePct > 100)
                errors.Add("Trigger.PriceChangePct must be 0-100");
            if (trigger.PriceChangeAbs < 0)
                errors.Add("Trigger.PriceChangeAbs must be >= 0");
            if (trigger.WindowSeconds <= 0)
                errors.Add("Trigger.WindowSeconds must be > 0");
            if (trigger.CooldownSeconds < 0)
                errors.Add("Trigger.CooldownSeconds must be >= 0");
        }

        var risk = policy.Risk;
        if (risk != null)
        {
            if (risk.MaxConsecutiveLosses < 0)
                errors.Add("Risk.MaxConsecutiveLosses must be >= 0");
            if (risk.CooldownMinutes < 0)
                errors.Add("Risk.CooldownMinutes must be >= 0");
            if (risk.StopLossPct < 0 || risk.StopLossPct > 100)
                errors.Add("Risk.StopLossPct must be 0-100");
            if (risk.TakeProfitPct < 0 || risk.TakeProfitPct > 100)
                errors.Add("Risk.TakeProfitPct must be 0-100");
        }

        var analysis = policy.Analysis;
        if (analysis != null)
        {
            if (analysis.SentimentWeight < 0 || analysis.SentimentWeight > 1)
                errors.Add("Analysis.SentimentWeight must be 0-1");
            if (analysis.TechnicalWeight < 0 || analysis.TechnicalWeight > 1)
                errors.Add("Analysis.TechnicalWeight must be 0-1");
            if (analysis.NewsWeight < 0 || analysis.NewsWeight > 1)
                errors.Add("Analysis.NewsWeight must be 0-1");
        }

        return errors;
    }
}
