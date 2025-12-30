using System.Text.Json;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - Market (行情)
//  - /capi/v2/market/*
//  - AI Wars 网关经常会对“无签名/无 UA”请求返回 HTML 403，所以这里统一 requiresAuth=true
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    public async Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        // GET /capi/v2/market/ticker?symbol=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/ticker",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            // AI Wars 环境下，很多节点会对“无签名/无 key 的请求”直接 403（甚至返回 HTML 网关页）。
            // 这里统一走签名请求，避免被网关当作未授权爬虫拦截。
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var obj = SelectObjectBySymbolOrFirst(payload, symbol);
        if (obj.ValueKind != JsonValueKind.Object)
            obj = payload;

        var tsMs = NormalizeUnixMs(ReadLong(obj, "ts", "timestamp", "time"));

        // WEEX AI Wars 实测字段：
        // - priceChangePercent: "-0.025387"  (通常表示 -2.5387%)
        //
        // 约定（与 proto 注释一致）：
        // - MarketTickEvent.Change24H = percent points（-2.5387 表示 -2.5387%）
        decimal change24h;
        var priceChangePercent = ReadDecimalNullable(obj, "priceChangePercent");
        if (priceChangePercent != null)
        {
            change24h = priceChangePercent.Value;
            if (Math.Abs(change24h) <= 1m)
                change24h *= 100m;
        }
        else
        {
            change24h = ReadDecimal(obj,
                "changeUtc24h",
                "change24h",
                "change_24h",
                "change",
                "chg24h",
                "chgRate",
                "changeRate",
                "changePercent",
                "rise");
            if (change24h == 0m)
            {
                // Best-effort fallback for unknown field names.
                change24h = ReadDecimalByNameContains(obj, "change", "chg", "rise") ?? 0m;
            }
        }

        // WEEX AI Wars 实测字段：
        // - base_volume: "64741.6673"          (base)
        // - volume_24h:  "5686363564.64648"    (quote)
        // 为避免把 quote volume 当成 base volume，我们优先 base_volume。
        var volume24h = ReadDecimal(obj, "base_volume");
        if (volume24h == 0m)
        {
            // Fallback: some tenants only provide quote volume.
            volume24h = ReadDecimal(obj, "volume_24h");
        }
        if (volume24h == 0m)
        {
            // Best-effort fallback for unknown field names.
            volume24h = ReadDecimalByNameContains(obj, "base_volume", "volume_24h", "volume", "vol", "turnover", "amount") ?? 0m;
        }

        return new TickerResponse
        {
            Symbol = symbol,
            LastPrice = ReadDecimal(obj, "last", "lastPrice", "close", "price"),
            BidPrice = ReadDecimal(obj, "best_bid", "bestBid", "bid", "bidPrice", "buy"),
            AskPrice = ReadDecimal(obj, "best_ask", "bestAsk", "ask", "askPrice", "sell"),
            Volume24h = volume24h,
            Change24h = change24h,
            High24h = ReadDecimal(obj, "high_24h", "high24h", "high"),
            Low24h = ReadDecimal(obj, "low_24h", "low24h", "low"),
            Timestamp = tsMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime
                : DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        // GET /capi/v2/market/candles?symbol=...&granularity=...&limit=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/candles",
            $"?symbol={Uri.EscapeDataString(symbol)}&granularity={Uri.EscapeDataString(interval)}&limit={limit}",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        if (payload.ValueKind != JsonValueKind.Array)
            return Array.Empty<KlineData>();

        var span = ParseIntervalToTimeSpanOrZero(interval);
        var list = new List<KlineData>();

        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array)
                continue;

            var parts = item.EnumerateArray().Select(ToInvariantString).ToList();
            if (parts.Count < 5)
                continue;

            var openMs = NormalizeUnixMs(ParseLong(parts[0]));
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime;

            list.Add(new KlineData
            {
                OpenTime = openTime,
                Open = ParseDecimal(parts.ElementAtOrDefault(1)),
                High = ParseDecimal(parts.ElementAtOrDefault(2)),
                Low = ParseDecimal(parts.ElementAtOrDefault(3)),
                Close = ParseDecimal(parts.ElementAtOrDefault(4)),
                Volume = ParseDecimal(parts.ElementAtOrDefault(5)),
                CloseTime = span > TimeSpan.Zero ? openTime.Add(span) : openTime
            });
        }

        return list;
    }

    // =========================================================================
    //  Market Rates (funding / open interest)
    // =========================================================================

    public async Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default)
    {
        // GET /capi/v2/market/currentFundRate?symbol=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/currentFundRate",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var obj = SelectObjectBySymbolOrFirst(payload, symbol);
        if (obj.ValueKind == JsonValueKind.Number && obj.TryGetDecimal(out var dn))
            return dn;
        if (obj.ValueKind == JsonValueKind.String && TryParseDecimalLoose(obj.GetString(), out var ds))
            return ds;
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        // Common keys observed in contract APIs (best-effort).
        // Funding rate is returned as **RATIO** (0.0001 means 0.01%).
        var raw = ReadDecimalNullable(obj, "fundingRate", "fundRate", "currentFundRate", "rate", "fund_rate")
                  ?? ReadDecimalByNameContains(obj, "fund", "rate");
        if (raw == null)
            return null;
        return raw.Value;
    }

    public async Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
    {
        // GET /capi/v2/market/open_interest?symbol=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/open_interest",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var obj = SelectObjectBySymbolOrFirst(payload, symbol);
        if (obj.ValueKind == JsonValueKind.Number && obj.TryGetDecimal(out var dn))
            return dn;
        if (obj.ValueKind == JsonValueKind.String && TryParseDecimalLoose(obj.GetString(), out var ds))
            return ds;
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        // WEEX AI Wars 实测（你贴的 raw）：
        // {
        //   "symbol": "cmt_btcusdt",
        //   "base_volume": "88273.0912",
        //   "target_volume": "88273.0912",
        //   "timestamp": "..."
        // }
        //
        // 因此这里优先读取 target_volume，其次 base_volume。
        return ReadDecimalNullable(obj, "target_volume", "targetVolume", "targetVol", "quote_volume", "quoteVolume")
               ?? ReadDecimalNullable(obj, "base_volume", "baseVolume", "baseVol")
               ?? ReadDecimalNullable(obj, "openInterest", "open_interest", "oi", "amount", "sumOpenInterest", "openInterestAmount")
               ?? ReadDecimalByNameContains(obj, "open", "interest", "oi", "volume");
    }

    private static JsonElement SelectObjectBySymbolOrFirst(JsonElement payload, string symbol)
    {
        // Some endpoints return:
        // - { data: { ... } }
        // - { data: [ {symbol:..., ...}, ... ] }
        // - { data: { "<symbol>": {...} } }

        if (payload.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            return payload;

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyInsensitive(payload, symbol, out var bySymbol) && bySymbol.ValueKind == JsonValueKind.Object)
                return bySymbol;
            return payload;
        }

        if (payload.ValueKind == JsonValueKind.Array)
        {
            JsonElement? firstObj = null;
            foreach (var item in payload.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                firstObj ??= item;
                var sym = ReadString(item, "symbol");
                if (!string.IsNullOrWhiteSpace(sym) && string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return firstObj ?? default;
        }

        return default;
    }
}


