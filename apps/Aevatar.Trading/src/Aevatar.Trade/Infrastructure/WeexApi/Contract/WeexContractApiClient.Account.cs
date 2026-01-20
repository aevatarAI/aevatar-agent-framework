using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - Account (账户/配置)
//  - /capi/v2/account/*
//  - 这里的核心职责：
//    - 余额（equity/available/frozen）
//    - 订单所需 marginMode 的“最佳努力”探测与自愈（40020）
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    public async Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
    {
        // GET /capi/v2/account/assets
        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/account/assets",
            "",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var items = ExtractObjectArrayDeep(payload);
        if (items.Count == 0)
            return Array.Empty<BalanceInfo>();

        var list = new List<BalanceInfo>();
        foreach (var item in items)
        {
            var currency = ReadString(item, "coinName", "currency", "coin") ?? "";
            if (string.IsNullOrWhiteSpace(currency))
                continue;

            list.Add(new BalanceInfo
            {
                Currency = currency,
                Balance = ReadDecimal(item, "equity", "balance", "total"),
                Available = ReadDecimal(item, "available", "availableBalance", "free"),
                Frozen = ReadDecimal(item, "frozen", "hold", "locked")
            });
        }

        return list;
    }

    public async Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct);
        return balances.FirstOrDefault(b => b.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(string? symbol = null, CancellationToken ct = default)
    {
        var filterSymbol = (symbol ?? "").Trim();

        // NOTE:
        // - 你在 curl 里验证"singlePosition"是对的，所以这里优先对齐：
        //   - 传了 symbol：GET /capi/v2/account/position/singlePosition?symbol=...
        //   - 没传 symbol：GET /capi/v2/account/position/allPosition
        JsonElement payload;
        if (!string.IsNullOrWhiteSpace(filterSymbol))
        {
            var root = await GetTradingAsync<JsonElement>(
                "/capi/v2/account/position/singlePosition",
                $"?symbol={Uri.EscapeDataString(filterSymbol)}",
                requiresAuth: true,
                ct);
            payload = UnwrapDataIfPresent(root);
        }
        else
        {
            var root = await GetTradingAsync<JsonElement>(
                "/capi/v2/account/position/allPosition",
                "",
                requiresAuth: true,
                ct);
            payload = UnwrapDataIfPresent(root);
        }

        var items = ExtractObjectArrayDeep(payload);
        if (items.Count == 0 && payload.ValueKind == JsonValueKind.Object)
        {
            // singlePosition 某些租户会直接返回“单个 object”，这里兜底成一条记录。
            items = new List<JsonElement> { payload };
        }
        if (items.Count == 0)
            return Array.Empty<PositionInfo>();

        var positions = new List<PositionInfo>();

        foreach (var item in items)
        {
            var sym = (ReadString(item, "symbol", "instId", "instrumentId", "contractCode", "contract_code") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sym))
                sym = "UNKNOWN";

            if (!string.IsNullOrWhiteSpace(filterSymbol) &&
                !string.Equals(sym, filterSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sideRaw = ReadString(item, "holdSide", "posSide", "positionSide", "side", "direction", "hold_side", "pos_side");
            var size = ReadDecimal(item, "size", "pos", "position", "positionAmt", "holdVol", "total", "qty", "quantity", "amount", "hold_vol", "position_amt");

            // WEEX AI Wars 字段：open_value（开仓价值）/ unrealizePnl（未实现盈亏，注意没有 'd'）
            var openValue = ReadDecimalNullable(item, "open_value", "openValue");
            var entryPrice = ReadDecimalNullable(item, "entryPrice", "openPrice", "avgOpenPrice", "avgPrice", "openAvgPrice", "open_avg_price", "avg_open_price");
            // 如果没有直接的 entryPrice，从 open_value / size 计算
            if (entryPrice == null && openValue.HasValue && size != 0m)
            {
                entryPrice = openValue.Value / Math.Abs(size);
            }
            
            var markPrice = ReadDecimalNullable(item, "markPrice", "marketPrice", "lastPrice", "price", "mark_price", "market_price", "last_price");
            // WEEX AI Wars 用 "unrealizePnl" 而不是 "unrealizedPnl"
            var unrealizedPnl = ReadDecimalNullable(item, "unrealizePnl", "unrealizedPnl", "upl", "unrealizedProfit", "floatingProfit", "pnl", "unrealized_pnl", "unrealisedPnl");
            var leverage = ReadDecimalNullable(item, "leverage", "lever");
            
            // 如果没有 markPrice，从 entryPrice 和 unrealizedPnl 反推
            // Long: markPrice = entryPrice + pnl / size
            // Short: markPrice = entryPrice - pnl / size
            var side = NormalizePositionSide(sideRaw);
            if (markPrice == null && entryPrice.HasValue && unrealizedPnl.HasValue && size != 0m)
            {
                var pnlPerUnit = unrealizedPnl.Value / Math.Abs(size);
                markPrice = side == "SHORT" || side == "SELL"
                    ? entryPrice.Value - pnlPerUnit
                    : entryPrice.Value + pnlPerUnit;
            }

            var notional = ReadDecimalNullable(item, "notional", "positionValue", "value", "marketValue", "position_value", "market_value", "open_value", "openValue");
            if (notional == null && markPrice.HasValue && size != 0m)
            {
                notional = Math.Abs(size) * markPrice.Value;
            }
            // 如果还是没有 notional，用 open_value 作为估算
            if (notional == null && openValue.HasValue)
            {
                notional = Math.Abs(openValue.Value);
            }

            // Skip empty rows
            if (size == 0m && unrealizedPnl == null && notional == null)
                continue;

            positions.Add(new PositionInfo
            {
                Symbol = sym,
                Side = side,
                Size = size,
                EntryPrice = entryPrice,
                MarkPrice = markPrice,
                UnrealizedPnl = unrealizedPnl,
                Notional = notional,
                Leverage = leverage
            });
        }

        return positions
            .OrderByDescending(p => Math.Abs(p.Notional ?? 0m))
            .ThenByDescending(p => Math.Abs(p.Size))
            .ToList();
    }

    // =========================
    //  Error code helpers
    // =========================

    private static bool IsBizCode(string message, string code)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(code))
            return false;

        // Typical shape: {"code":"40020","msg":"..."}
        return message.Contains($"\"code\":\"{code}\"", StringComparison.OrdinalIgnoreCase) ||
               message.Contains($"\"code\":{code}", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePositionSide(string? raw)
    {
        var v = (raw ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(v))
            return "UNKNOWN";

        if (v is "LONG" or "BUY" or "OPEN_LONG")
            return "LONG";
        if (v is "SHORT" or "SELL" or "OPEN_SHORT")
            return "SHORT";
        if (v is "CLOSE_LONG")
            return "SELL";
        if (v is "CLOSE_SHORT")
            return "BUY";

        // Some tenants use numeric enums; keep as-is to avoid lying.
        return v;
    }

    // =========================
    //  Account settings (marginMode)
    // =========================

    private static IReadOnlyList<int?> BuildMarginModeCandidates(int? current, int detected)
    {
        var list = new List<int?>();

        void Add(int? v)
        {
            if (!list.Contains(v))
                list.Add(v);
        }

        if (detected > 0) Add(detected);
        Add(current);

        // Most common documented values.
        Add(1);
        Add(3);

        // Some gateways may use 0/2 style enums; keep as last-resort.
        Add(0);
        Add(2);

        // Finally: omit marginMode entirely (some gateways infer from account mode).
        Add(null);

        return list;
    }

    // =========================
    //  Account config mutation (self-heal for 40020)
    // =========================

    private async Task TryChangeHoldModelAsync(string symbol, int marginMode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return;

        // POST /capi/v2/account/position/changeHoldModel
        // Required: symbol, marginMode
        var body = new Dictionary<string, object?>
        {
            ["symbol"] = symbol,
            ["marginMode"] = marginMode
        };

        var raw = await PostTradingRawAsync(
            "/capi/v2/account/position/changeHoldModel",
            "",
            body,
            requiresAuth: true,
            ct);

        Logger.LogInformation(
            "[WeexContract] changeHoldModel submitted. Symbol={Symbol}, marginMode={Mode}, resp={Resp}",
            symbol, marginMode, Truncate(raw, 260));
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        if (s.Length <= max)
            return s;
        return s.Substring(0, max) + "...";
    }

    private async Task<int> GetAccountMarginModeAsync(string symbol, bool forceRefresh, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return 0;

        if (!forceRefresh &&
            _marginModeCache.TryGetValue(symbol, out var cached) &&
            cached > 0)
        {
            return cached;
        }

        try
        {
            // GET /capi/v2/account/settings?symbol=...
            var root = await GetTradingAsync<JsonElement>(
                "/capi/v2/account/settings",
                $"?symbol={Uri.EscapeDataString(symbol)}",
                requiresAuth: true,
                ct);

            var payload = UnwrapDataIfPresent(root);
            var info = PickAccountSettingsInfo(payload, symbol);
            if (info == null)
                return 0;

            var modeLong = ReadLong(info.Value, "marginMode", "margin_mode", "marginmode");
            var mode = (int)modeLong;
            if (mode > 0)
            {
                _marginModeCache[symbol] = mode;
                return mode;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "[WeexContract] Failed to fetch account settings marginMode for {Symbol}", symbol);
        }

        return 0;
    }

    private static JsonElement? PickAccountSettingsInfo(JsonElement payload, string symbol)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            // If marginMode is directly present, use this object.
            if (TryGetPropertyInsensitive(payload, "marginMode", out _) ||
                TryGetPropertyInsensitive(payload, "margin_mode", out _) ||
                TryGetPropertyInsensitive(payload, "marginmode", out _))
            {
                return payload;
            }

            // Some responses wrap the actual list inside another object:
            // { list: [...] } / { result: [...] } / { rows: [...] } ...
            foreach (var key in new[] { "list", "result", "items", "rows", "records", "data" })
            {
                if (TryGetPropertyInsensitive(payload, key, out var inner) &&
                    (inner.ValueKind == JsonValueKind.Array || inner.ValueKind == JsonValueKind.Object))
                {
                    var picked = PickAccountSettingsInfo(inner, symbol);
                    if (picked != null)
                        return picked;
                }
            }

            // Fallback: return object as-is.
            return payload;
        }

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
}


