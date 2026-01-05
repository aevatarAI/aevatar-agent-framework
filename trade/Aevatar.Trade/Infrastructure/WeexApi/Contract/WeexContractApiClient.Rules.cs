using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - Rules (规则/对齐)
//  - stepSize: 从 /capi/v2/market/contracts 拉取并缓存，保证 size 满足交易所精度
//  - 40015: 从错误信息中解析 stepSize 再重试一次（兜底）
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    private async Task<decimal> GetContractSizeStepAsync(string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return 0m;

        if (_sizeStepCache.TryGetValue(symbol, out var cached) && cached > 0)
            return cached;

        try
        {
            var root = await GetMarketAsync<JsonElement>(
                "/capi/v2/market/contracts",
                $"?symbol={Uri.EscapeDataString(symbol)}",
                requiresAuth: true,
                ct);

            var payload = UnwrapDataIfPresent(root);
            var info = PickContractInfo(payload, symbol);
            if (info == null)
                return 0m;

            var step = ReadDecimal(info.Value, "stepSize", "step_size", "sizeStep", "qtyStep", "quantityStep");
            if (step > 0)
            {
                _sizeStepCache[symbol] = step;
                return step;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // best-effort only
            Logger.LogDebug(ex, "[WeexContract] Failed to fetch contract stepSize for {Symbol}", symbol);
        }

        return 0m;
    }

    private static JsonElement? PickContractInfo(JsonElement payload, string symbol)
    {
        if (payload.ValueKind == JsonValueKind.Object)
            return payload;

        if (payload.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? first = null;
        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            first ??= item;

            if (TryGetPropertyInsensitive(item, "symbol", out var symEl) && symEl.ValueKind == JsonValueKind.String)
            {
                var s = symEl.GetString();
                if (string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        return first;
    }

    private static bool TryParseDecimalInvariant(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return decimal.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDecimalInvariant(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static decimal RoundUpToStep(decimal value, decimal step)
    {
        if (step <= 0) return value;
        if (value <= 0) return value;

        var q = value / step;
        var qi = decimal.Truncate(q);
        if (qi * step < value)
            qi += 1;
        return qi * step;
    }

    private static bool TryExtractStepSizeFromError(string message, out decimal step)
    {
        step = 0m;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // Example:
        // INVALID_ARGUMENT: ... matches the stepSize '0.0001' requirement. Current size is '0.000111'
        const string marker = "stepSize '";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        idx += marker.Length;
        var end = message.IndexOf('\'', idx);
        if (end <= idx)
            return false;

        var raw = message.Substring(idx, end - idx).Trim();
        if (!TryParseDecimalInvariant(raw, out var parsed) || parsed <= 0)
            return false;

        step = parsed;
        return true;
    }
}


