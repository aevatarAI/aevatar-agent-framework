using System.Globalization;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Policy;

// ============================================================================
//  Trading Policy Tools
//  - 提供 get/update 工具供 AI 调整策略参数
// ============================================================================

internal sealed class GetTradingPolicyTool : AevatarToolBase
{
    private readonly Func<TradingPolicyConfig> _getPolicy;

    public GetTradingPolicyTool(Func<TradingPolicyConfig> getPolicy)
    {
        _getPolicy = getPolicy ?? throw new ArgumentNullException(nameof(getPolicy));
    }

    public override string Name => "get_trading_policy";
    public override string Description => "Get current trading policy snapshot.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new();

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IMessage>(_getPolicy());
    }
}

internal sealed class UpdateTradingPolicyTool : AevatarToolBase
{
    private readonly Func<TradingPolicyConfig> _getPolicy;
    private readonly Func<TradingPolicyConfig, string?, string?, CancellationToken, Task<TradingPolicyUpdatedEvent>> _applyPolicy;

    public UpdateTradingPolicyTool(
        Func<TradingPolicyConfig> getPolicy,
        Func<TradingPolicyConfig, string?, string?, CancellationToken, Task<TradingPolicyUpdatedEvent>> applyPolicy)
    {
        _getPolicy = getPolicy ?? throw new ArgumentNullException(nameof(getPolicy));
        _applyPolicy = applyPolicy ?? throw new ArgumentNullException(nameof(applyPolicy));
    }

    public override string Name => "update_trading_policy";
    public override string Description => "Update trading policy by patching provided fields.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["policy"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["policy"] = new()
            {
                Type = "object",
                Description = "Policy patch object: { trading?, risk?, trigger?, analysis? }",
                Required = true
            },
            ["reason"] = new()
            {
                Type = "string",
                Description = "Optional update reason",
                Required = false,
                MaxLength = 500
            },
            ["updatedBy"] = new()
            {
                Type = "string",
                Description = "Optional updater identity",
                Required = false,
                MaxLength = 200
            }
        }
    };

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var policyObject = GetRequiredObject(parameters, "policy");
        var current = _getPolicy();
        var next = TradingPolicyPatch.Apply(current, policyObject);

        var reason = parameters.GetValueOrDefault("reason")?.ToString();
        var updatedBy = parameters.GetValueOrDefault("updatedBy")?.ToString();
        if (string.IsNullOrWhiteSpace(updatedBy))
            updatedBy = context.AgentId;

        return await _applyPolicy(next, updatedBy, reason, cancellationToken);
    }

    private static JsonElement GetRequiredObject(Dictionary<string, object> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not JsonElement obj || obj.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} is required and must be an object");
        return obj;
    }
}

// ============================================================================
//  Patch helpers
// ============================================================================

internal static class TradingPolicyPatch
{
    public static TradingPolicyConfig Apply(TradingPolicyConfig current, JsonElement patch)
    {
        var next = current.Clone();

        if (TryGetObject(patch, "trading", out var trading))
            ApplyTrading(next.Trading ??= new TradingConfig(), trading);
        if (TryGetObject(patch, "risk", out var risk))
            ApplyRisk(next.Risk ??= new RiskControlConfig(), risk);
        if (TryGetObject(patch, "trigger", out var trigger))
            ApplyTrigger(next.Trigger ??= new DecisionTriggerConfig(), trigger);
        if (TryGetObject(patch, "analysis", out var analysis))
            ApplyAnalysis(next.Analysis ??= new AnalysisWeightConfig(), analysis);

        return next;
    }

    private static void ApplyTrading(TradingConfig target, JsonElement obj)
    {
        if (TryGetString(obj, "symbol", out var symbol)) target.Symbol = symbol;
        if (TryGetString(obj, "interval", out var interval)) target.Interval = interval;
        if (TryGetDouble(obj, "maxPositionPct", out var maxPos)) target.MaxPositionPct = maxPos;
        if (TryGetDouble(obj, "maxTotalPositionPct", out var maxTotal)) target.MaxTotalPositionPct = maxTotal;
        if (TryGetDouble(obj, "maxLossPerTrade", out var maxLoss)) target.MaxLossPerTrade = maxLoss;
        if (TryGetDouble(obj, "maxDailyLoss", out var maxDaily)) target.MaxDailyLoss = maxDaily;
        if (TryGetInt(obj, "minConfidenceToTrade", out var minConf)) target.MinConfidenceToTrade = minConf;
        if (TryGetDouble(obj, "minBaseAssetUsdOnStart", out var minBase)) target.MinBaseAssetUsdOnStart = minBase;
        if (TryGetString(obj, "executionMode", out var mode) && TryParseExecutionMode(mode, out var parsed))
            target.ExecutionMode = parsed;
    }

    private static void ApplyRisk(RiskControlConfig target, JsonElement obj)
    {
        if (TryGetInt(obj, "maxConsecutiveLosses", out var maxLosses)) target.MaxConsecutiveLosses = maxLosses;
        if (TryGetInt(obj, "cooldownMinutes", out var cooldown)) target.CooldownMinutes = cooldown;
        if (TryGetDouble(obj, "stopLossPct", out var stopLoss)) target.StopLossPct = stopLoss;
        if (TryGetDouble(obj, "takeProfitPct", out var takeProfit)) target.TakeProfitPct = takeProfit;
    }

    private static void ApplyTrigger(DecisionTriggerConfig target, JsonElement obj)
    {
        if (TryGetDouble(obj, "priceChangePct", out var pct)) target.PriceChangePct = pct;
        if (TryGetDouble(obj, "priceChangeAbs", out var abs)) target.PriceChangeAbs = abs;
        if (TryGetInt(obj, "windowSeconds", out var window)) target.WindowSeconds = window;
        if (TryGetInt(obj, "cooldownSeconds", out var cooldown)) target.CooldownSeconds = cooldown;
        if (TryGetBool(obj, "triggerOnStartup", out var triggerOnStartup)) target.TriggerOnStartup = triggerOnStartup;
    }

    private static void ApplyAnalysis(AnalysisWeightConfig target, JsonElement obj)
    {
        if (TryGetDouble(obj, "sentimentWeight", out var sentiment)) target.SentimentWeight = sentiment;
        if (TryGetDouble(obj, "technicalWeight", out var technical)) target.TechnicalWeight = technical;
        if (TryGetDouble(obj, "newsWeight", out var news)) target.NewsWeight = news;
    }

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object) return true;
        var pascal = ToPascal(name);
        if (element.TryGetProperty(pascal, out value) && value.ValueKind == JsonValueKind.Object) return true;
        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return value.Length > 0;
        }
        var pascal = ToPascal(name);
        if (element.TryGetProperty(pascal, out prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return value.Length > 0;
        }
        value = "";
        return false;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        if (TryGetNumber(element, name, out var number))
        {
            value = (int)Math.Round(number);
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        if (TryGetNumber(element, name, out var number))
        {
            value = number;
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }
        var pascal = ToPascal(name);
        if (element.TryGetProperty(pascal, out prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }
        value = false;
        return false;
    }

    private static bool TryGetNumber(JsonElement element, string name, out double value)
    {
        if (TryGetNumberValue(element, name, out value)) return true;
        var pascal = ToPascal(name);
        return TryGetNumberValue(element, pascal, out value);
    }

    private static bool TryGetNumberValue(JsonElement element, string name, out double value)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            value = 0;
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
            return true;

        if (prop.ValueKind == JsonValueKind.String &&
            double.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;

        value = 0;
        return false;
    }

    private static string ToPascal(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static bool TryParseExecutionMode(string value, out TradeExecutionMode mode)
    {
        if (Enum.TryParse(value, ignoreCase: true, out mode))
            return true;

        if (string.Equals(value, "DryRun", StringComparison.OrdinalIgnoreCase))
        {
            mode = TradeExecutionMode.DryRun;
            return true;
        }

        if (string.Equals(value, "Live", StringComparison.OrdinalIgnoreCase))
        {
            mode = TradeExecutionMode.Live;
            return true;
        }

        mode = TradeExecutionMode.Unspecified;
        return false;
    }
}
