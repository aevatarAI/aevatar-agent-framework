using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - Trading (交易)
//  - /capi/v2/order/*
//  - 重点：
//    - 下单：marginMode 自适应（40020） + stepSize 自适应（40015）
//    - 撤单/查询：统一 JSON 解析
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    public async Task<IReadOnlyList<FillInfo>> GetFillsAsync(string? symbol = null, int limit = 50, CancellationToken ct = default)
    {
        // GET /capi/v2/order/fills
        var lim = limit <= 0 ? 50 : Math.Min(limit, 100);
        var sym = (symbol ?? "").Trim();

        var parts = new List<string>(capacity: 2);
        if (!string.IsNullOrWhiteSpace(sym))
            parts.Add($"symbol={Uri.EscapeDataString(sym)}");
        parts.Add($"limit={lim}");

        var query = "?" + string.Join("&", parts);

        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/order/fills",
            query,
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var items = ExtractObjectArrayDeep(payload);
        if (items.Count == 0)
            return Array.Empty<FillInfo>();

        var fills = new List<FillInfo>();
        foreach (var item in items)
        {
            var fillSymbol = (ReadString(item, "symbol", "instId", "instrumentId", "contractCode", "contract_code") ?? "").Trim();
            var sideRaw = ReadString(item, "side", "direction", "tradeSide", "trade_side", "type");
            var orderId = ReadString(item, "order_id", "orderId");

            var price = ReadDecimalNullable(
                item,
                "price",
                "fillPrice",
                "fill_price",
                "tradePrice",
                "trade_price",
                "dealPrice",
                "deal_price",
                "price_avg",
                "priceAvg",
                "avgPrice");

            var qty = ReadDecimalNullable(
                item,
                "qty",
                "quantity",
                "size",
                "vol",
                "volume",
                "amount",
                "fillQty",
                "fill_qty",
                "dealQty",
                "deal_qty",
                "tradeQty",
                "trade_qty",
                "filled_qty",
                "filledQty",
                "filledSize");
            var fee = ReadDecimalNullable(item, "fee", "fees");

            var ts = ReadLongNullable(item, "ts", "timestamp", "time", "createTime", "tradeTime", "fillTime", "cTime", "uTime");
            long? tsMs = null;
            DateTime? timeUtc = null;
            if (ts.HasValue && ts.Value > 0)
            {
                var ms = NormalizeUnixMs(ts.Value);
                if (ms > 0)
                {
                    tsMs = ms;
                    timeUtc = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                }
            }

            if (string.IsNullOrWhiteSpace(fillSymbol) && price == null && qty == null && timeUtc == null)
                continue;

            fills.Add(new FillInfo
            {
                Ts = tsMs,
                TimeUtc = timeUtc,
                Symbol = string.IsNullOrWhiteSpace(fillSymbol) ? null : fillSymbol,
                Side = NormalizeFillSide(sideRaw),
                Price = price,
                Quantity = qty,
                OrderId = orderId,
                Fee = fee
            });
        }

        return fills
            .OrderBy(f => f.TimeUtc.HasValue ? 0 : 1)
            .ThenBy(f => f.TimeUtc ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        // --------------------------------------------------------------
        //  AI Wars 合约下单（Live 自动交易链路的核心）
        //  POST /capi/v2/order/placeOrder
        //  body: { symbol, client_oid, size, type, order_type, match_price, price, marginMode }
        //
        //  IMPORTANT:
        //  - WEEX 会校验 size 的 stepSize（例如 BTC: 0.0001）。
        //  - 为了让 “AI 自动交易 + Startup guard” 稳定，这里会在发送前把 size 向上取整到 stepSize 的倍数。
        // --------------------------------------------------------------
        var clientOid = request.ClientOrderId ?? GenerateClientOrderId();

        var isLimit = string.Equals(request.OrderType, "limit", StringComparison.OrdinalIgnoreCase);
        var isMarket = string.Equals(request.OrderType, "market", StringComparison.OrdinalIgnoreCase);
        if (!isLimit && !isMarket)
        {
            return new OrderResult
            {
                Success = false,
                ErrorCode = "UNSUPPORTED",
                ErrorMessage = $"Unsupported orderType: {request.OrderType}. Use limit or market."
            };
        }

        // Force -> contract order_type mapping:
        // 0: Normal, 1: Post-Only, 2: Fill-Or-Kill, 3: Immediate Or Cancel
        var orderType = (request.Force ?? "normal").Trim() switch
        {
            var v when v.Equals("postOnly", StringComparison.OrdinalIgnoreCase) => "1",
            var v when v.Equals("fok", StringComparison.OrdinalIgnoreCase) => "2",
            var v when v.Equals("ioc", StringComparison.OrdinalIgnoreCase) => "3",
            _ => "0"
        };

        // match_price:
        // 0: Limit price, 1: Market price
        var matchPrice = isMarket ? "1" : "0";

        // docs sometimes still require "price" field even for market; use 0 as safe default
        var price = isMarket
            ? (string.IsNullOrWhiteSpace(request.Price) ? "0" : request.Price)
            : request.Price;

        if (isLimit && string.IsNullOrWhiteSpace(price))
        {
            return new OrderResult
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = "Limit order requires price."
            };
        }

        // buy  -> 1 (open long) ; sell -> 2 (open short)
        var type = string.Equals(request.Side, "buy", StringComparison.OrdinalIgnoreCase) ? "1" : "2";

        // MarginMode:
        // - Accounts may be configured as Cross/Isolated and will reject mismatched requests (40020).
        // - Some tenants do NOT expose marginMode via /account/settings; discovery = try candidates.
        int? marginMode = null;
        var marginModeFromCache = false;
        if (_marginModeCache.TryGetValue(request.Symbol, out var cachedMarginMode))
        {
            marginModeFromCache = true;
            // -1 means "omit"
            marginMode = cachedMarginMode == -1 ? null : cachedMarginMode;
        }
        else
        {
            var detected = await GetAccountMarginModeAsync(request.Symbol, forceRefresh: false, ct);
            if (detected > 0)
                marginMode = detected;
        }

        // Default to Cross (1) if unknown AND not explicitly cached as "<omitted>".
        if (!marginModeFromCache && !marginMode.HasValue)
            marginMode = 1;

        var marginModeAttempts = new List<string>();
        static string FormatMarginMode(int? m) => m.HasValue ? m.Value.ToString() : "<omitted>";

        // Align size to stepSize (best-effort).
        var sizeStr = request.Quantity;
        if (!TryParseDecimalInvariant(sizeStr, out var sizeDec) || sizeDec <= 0)
        {
            return new OrderResult
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = $"Invalid quantity: {request.Quantity}"
            };
        }

        // Step-size rounding (best-effort).
        var stepFromRules = await GetContractSizeStepAsync(request.Symbol, ct);
        if (stepFromRules > 0)
        {
            var rounded = RoundUpToStep(sizeDec, stepFromRules);
            if (rounded > 0 && rounded != sizeDec)
            {
                Logger.LogDebug(
                    "[WeexContract] Adjust size to stepSize (rules): {Symbol} size {Old} -> {New} (step={Step})",
                    request.Symbol, sizeDec, rounded, stepFromRules);
                sizeDec = rounded;
            }
        }

        sizeStr = FormatDecimalInvariant(sizeDec);

        async Task<OrderResult> PlaceOnceAsync(string size, int? marginModeValue)
        {
            var body = new
            {
                symbol = request.Symbol,
                client_oid = clientOid,
                size,
                type,
                order_type = orderType,
                match_price = matchPrice,
                price,
                marginMode = marginModeValue
            };

            var raw = await PostTradingRawAsync(
                "/capi/v2/order/placeOrder",
                "",
                body,
                requiresAuth: true,
                ct);

            using var doc = JsonDocument.Parse(raw);
            var payload = UnwrapDataIfPresent(doc.RootElement);
            var placed = payload.Deserialize<ContractPlaceOrderResponse>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(placed?.OrderId))
            {
                return new OrderResult
                {
                    Success = true,
                    OrderId = placed!.OrderId,
                    ClientOrderId = placed.ClientOid ?? clientOid
                };
            }

            return new OrderResult
            {
                Success = false,
                ErrorCode = "WEEX_CONTRACT_ERROR",
                ErrorMessage = $"Unexpected contract response: {raw}"
            };
        }

        try
        {
            // ------------------------------------------------------------------
            //  Retry policy (single placeOrder):
            //  - stepSize: retry at most once (parse stepSize from error if needed)
            //  - marginMode: try a candidate set (including omit)
            //  - if still 40020: attempt changeHoldModel (1/3) then retry
            // ------------------------------------------------------------------

            void CacheMarginMode(string symbol, int? candidate)
            {
                // -1 means omit
                _marginModeCache[symbol] = candidate.HasValue ? candidate.Value : -1;
            }

            var stepRetriedFromError = false;
            var detected = await GetAccountMarginModeAsync(request.Symbol, forceRefresh: true, ct);
            var candidates = BuildMarginModeCandidates(marginMode, detected);

            WeexApiException? last = null;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                marginModeAttempts.Add(FormatMarginMode(candidate));

                try
                {
                    var ok = await PlaceOnceAsync(sizeStr, candidate);
                    CacheMarginMode(request.Symbol, candidate);
                    Logger.LogInformation(
                        "[WeexContract] PlaceOrder OK. Symbol={Symbol}, size={Size}, marginMode={Mode}",
                        request.Symbol, sizeStr, FormatMarginMode(candidate));
                    return ok;
                }
                catch (WeexApiException ex)
                {
                    last = ex;

                    // 1) stepSize mismatch (40015): retry once with rounded size, then continue marginMode discovery.
                    if (!stepRetriedFromError &&
                        TryExtractStepSizeFromError(ex.Message, out var stepFromError) &&
                        stepFromError > 0 &&
                        TryParseDecimalInvariant(sizeStr, out var originalDec) &&
                        originalDec > 0)
                    {
                        var rounded = RoundUpToStep(originalDec, stepFromError);
                        var retrySize = FormatDecimalInvariant(rounded);
                        if (rounded > 0 && !string.Equals(retrySize, sizeStr, StringComparison.Ordinal))
                        {
                            _sizeStepCache[request.Symbol] = stepFromError;
                            stepRetriedFromError = true;

                            Logger.LogWarning(
                                "[WeexContract] PlaceOrder stepSize mismatch; retry with rounded size. Symbol={Symbol}, old={Old}, new={New}, step={Step}",
                                request.Symbol, sizeStr, retrySize, stepFromError);

                            sizeStr = retrySize;
                            i -= 1; // retry same marginMode candidate with new size
                            continue;
                        }
                    }

                    // 2) marginMode mismatch (40020): try next candidate.
                    if (IsBizCode(ex.Message, "40020"))
                        continue;

                    // Other errors: stop.
                    Logger.LogError(ex, "Failed to place AI Wars contract order for {Symbol}", request.Symbol);
                    return new OrderResult
                    {
                        Success = false,
                        ErrorCode = ex.ErrorCode ?? "CLIENT_ERROR",
                        ErrorMessage = $"{ex.Message} (marginMode tried: {string.Join(", ", marginModeAttempts.Distinct(StringComparer.OrdinalIgnoreCase))})"
                    };
                }
            }

            // 3) Still 40020 after candidates -> attempt changeHoldModel (1/3), then place again.
            if (last != null && IsBizCode(last.Message, "40020"))
            {
                foreach (var mode in new[] { 1, 3 })
                {
                    try
                    {
                        marginModeAttempts.Add($"changeHoldModel({mode})");
                        await TryChangeHoldModelAsync(request.Symbol, mode, ct);

                        // Give the gateway a short moment to apply the new mode.
                        await Task.Delay(TimeSpan.FromMilliseconds(900), ct);

                        marginModeAttempts.Add(FormatMarginMode(mode));
                        var ok = await PlaceOnceAsync(sizeStr, mode);
                        CacheMarginMode(request.Symbol, mode);
                        Logger.LogInformation(
                            "[WeexContract] PlaceOrder OK after changeHoldModel. Symbol={Symbol}, size={Size}, marginMode={Mode}",
                            request.Symbol, sizeStr, mode);
                        return ok;
                    }
                    catch (WeexApiException ex)
                    {
                        last = ex;
                        if (!IsBizCode(ex.Message, "40020"))
                            break;
                    }
                }
            }

            // Exhausted all attempts.
            var finalEx = last ?? new WeexApiException("Unknown error", errorCode: "CLIENT_ERROR");
            Logger.LogError(finalEx, "Failed to place AI Wars contract order for {Symbol}", request.Symbol);
            return new OrderResult
            {
                Success = false,
                ErrorCode = finalEx.ErrorCode ?? "CLIENT_ERROR",
                ErrorMessage =
                    $"{finalEx.Message} (marginMode tried: {string.Join(", ", marginModeAttempts.Distinct(StringComparer.OrdinalIgnoreCase))}, size={sizeStr})"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to place AI Wars contract order for {Symbol}", request.Symbol);
            return new OrderResult
            {
                Success = false,
                ErrorCode = "CLIENT_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CancelOrderResult> CancelOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
    {
        // POST /capi/v2/order/cancel_order
        var body = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(orderId))
            body["orderId"] = orderId;
        if (!string.IsNullOrWhiteSpace(clientOrderId))
            body["clientOid"] = clientOrderId;

        if (body.Count == 0)
        {
            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = "Either orderId or clientOrderId is required."
            };
        }

        try
        {
            var raw = await PostTradingRawAsync(
                "/capi/v2/order/cancel_order",
                "",
                body,
                requiresAuth: true,
                ct);

            using var doc = JsonDocument.Parse(raw);
            var payload = UnwrapDataIfPresent(doc.RootElement);
            var dto = payload.Deserialize<CancelOrderDto>(JsonOptions);

            if (dto?.Result == true)
            {
                return new CancelOrderResult
                {
                    Success = true,
                    OrderId = dto.OrderId,
                    ClientOrderId = dto.ClientOid
                };
            }

            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = dto?.ErrCode ?? "WEEX_CONTRACT_ERROR",
                ErrorMessage = dto?.ErrMsg ?? $"Unexpected contract response: {raw}"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel AI Wars contract order {OrderId}", orderId ?? clientOrderId);
            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = "CLIENT_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OrderInfo?> GetOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
    {
        // GET /capi/v2/order/detail?orderId=...
        if (string.IsNullOrWhiteSpace(orderId))
            throw new WeexApiException("AI Wars contract order detail requires orderId.", errorCode: "INVALID_REQUEST");

        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/order/detail",
            $"?orderId={Uri.EscapeDataString(orderId)}",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        return MapContractOrder(payload, symbolFallback: symbol);
    }

    public async Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default)
    {
        // GET /capi/v2/order/current
        var query = string.IsNullOrWhiteSpace(symbol) ? "" : $"?symbol={Uri.EscapeDataString(symbol)}";
        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/order/current",
            query,
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var orders = ExtractContractOrderArray(payload);

        return orders
            .Select(o => MapContractOrder(o, symbolFallback: symbol ?? ""))
            .Where(o => o != null)
            .Cast<OrderInfo>()
            .ToList();
    }

    private static string NormalizeFillSide(string? raw)
    {
        var v = (raw ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(v))
            return "UNKNOWN";

        if (v is "BUY" or "LONG" or "OPEN_LONG")
            return "BUY";
        if (v is "SELL" or "SHORT" or "OPEN_SHORT")
            return "SELL";
        if (v is "CLOSE_LONG")
            return "SELL";
        if (v is "CLOSE_SHORT")
            return "BUY";

        return v;
    }
}


