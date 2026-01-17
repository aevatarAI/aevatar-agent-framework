using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Triggers;

// ============================================================================
//  DecisionTriggerAgent
//  - 监听价格变化并触发决策事件
// ============================================================================

public sealed class DecisionTriggerAgent : GAgentBase<DecisionTriggerState>
{
    private readonly Dictionary<string, DateTime> _windowStartUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _windowBasePrice = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastTriggerUtc = new(StringComparer.OrdinalIgnoreCase);

    private DecisionTriggerConfig _config = new();

    public void Configure(DecisionTriggerConfig config)
    {
        _config = config?.Clone() ?? new DecisionTriggerConfig();
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        State.AgentId = Id.ToString();
        Logger.LogInformation("[DecisionTrigger] Activated: {AgentId}", State.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"DecisionTrigger: Triggers={State.TriggersEmitted}, Window={EffectiveWindowSeconds()}s, Cooldown={EffectiveCooldownSeconds()}s");
    }

    // ============ Event Handlers ============

    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Symbol))
            return;

        var symbol = evt.Symbol.Trim();
        var price = evt.Price;
        if (price <= 0)
            return;

        State.LastPrices[symbol] = price;

        var now = DateTime.UtcNow;
        var windowSeconds = EffectiveWindowSeconds();
        var windowStart = GetOrInitWindowStart(symbol, now);
        if ((now - windowStart).TotalSeconds >= windowSeconds)
        {
            ResetWindow(symbol, price, now);
            return;
        }

        var basePrice = GetOrInitBasePrice(symbol, price);
        if (!TryEvaluateTrigger(basePrice, price, out var deltaPct, out var deltaAbs))
            return;

        if (!IsCooldownReady(symbol, now))
            return;

        _lastTriggerUtc[symbol] = now;
        ResetWindow(symbol, price, now);

        State.LastTriggerSymbol = symbol;
        State.LastTriggerReason = "PRICE_CHANGE";
        State.LastTriggerPrice = price;
        State.LastTriggerDeltaPct = deltaPct;
        State.LastTriggerDeltaAbs = deltaAbs;
        State.LastTriggerTime = Timestamp.FromDateTime(now);
        State.TriggersEmitted += 1;

        var triggerEvent = new DecisionTriggerEvent
        {
            TriggerId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Reason = "PRICE_CHANGE",
            DeltaPct = deltaPct,
            DeltaAbs = deltaAbs,
            BasePrice = basePrice,
            LatestPrice = price,
            Timestamp = Timestamp.FromDateTime(now)
        };

        Logger.LogInformation(
            "[DecisionTrigger] Triggered: {Symbol} Δ%={DeltaPct:F4}, Δ={DeltaAbs:F4}",
            symbol, deltaPct, deltaAbs);

        await PublishAsync(triggerEvent, Aevatar.Agents.EventDirection.Up);
    }

    [EventHandler]
    public Task HandleTradingPolicyUpdated(TradingPolicyUpdatedEvent evt)
    {
        if (evt.Policy?.Trigger != null)
        {
            Configure(evt.Policy.Trigger);
            Logger.LogInformation("[DecisionTrigger] Config updated by policy");
        }

        return Task.CompletedTask;
    }

    // ============ Internal ============

    private int EffectiveWindowSeconds()
    {
        return _config.WindowSeconds <= 0 ? 30 : _config.WindowSeconds;
    }

    private int EffectiveCooldownSeconds()
    {
        return _config.CooldownSeconds < 0 ? 0 : _config.CooldownSeconds;
    }

    private DateTime GetOrInitWindowStart(string symbol, DateTime now)
    {
        if (_windowStartUtc.TryGetValue(symbol, out var start))
            return start;

        _windowStartUtc[symbol] = now;
        return now;
    }

    private double GetOrInitBasePrice(string symbol, double price)
    {
        if (_windowBasePrice.TryGetValue(symbol, out var basePrice))
            return basePrice;

        _windowBasePrice[symbol] = price;
        return price;
    }

    private void ResetWindow(string symbol, double price, DateTime now)
    {
        _windowStartUtc[symbol] = now;
        _windowBasePrice[symbol] = price;
    }

    private bool TryEvaluateTrigger(double basePrice, double latestPrice, out double deltaPct, out double deltaAbs)
    {
        deltaAbs = latestPrice - basePrice;
        deltaPct = basePrice <= 0 ? 0 : (deltaAbs / basePrice) * 100;

        var pctThreshold = _config.PriceChangePct;
        var absThreshold = _config.PriceChangeAbs;
        var pctEnabled = pctThreshold > 0;
        var absEnabled = absThreshold > 0;
        if (!pctEnabled && !absEnabled)
            return false;

        if (pctEnabled && Math.Abs(deltaPct) >= pctThreshold)
            return true;

        return absEnabled && Math.Abs(deltaAbs) >= absThreshold;
    }

    private bool IsCooldownReady(string symbol, DateTime now)
    {
        var cooldown = EffectiveCooldownSeconds();
        if (cooldown == 0)
            return true;

        if (!_lastTriggerUtc.TryGetValue(symbol, out var last))
            return true;

        return (now - last).TotalSeconds >= cooldown;
    }
}
