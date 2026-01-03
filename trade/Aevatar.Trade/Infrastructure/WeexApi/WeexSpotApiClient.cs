using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Spot API Client
//  - 对应 Spot 风格：/api/v2/...
//  - 仅在 Weex:Mode=Spot 时注入为 IWeexApiClient
// ============================================================================

internal sealed class WeexSpotApiClient : WeexApiClientBase, IWeexApiClient
{
    public WeexSpotApiClient(
        HttpClient httpClient,
        IOptions<WeexApiConfig> config,
        ILogger<WeexSpotApiClient> logger)
        : base(httpClient, config, logger)
    {
    }

    // =========================
    //  Market Data
    // =========================

    public async Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        var response = await GetMarketAsync<WeexResponse<TickerDto>>(
            "/api/v2/market/ticker",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            requiresAuth: false,
            ct);

        var data = response.Data ?? throw new WeexApiException("Empty ticker response");
        return new TickerResponse
        {
            Symbol = symbol,
            LastPrice = ParseDecimal(data.Last),
            BidPrice = ParseDecimal(data.BestBid),
            AskPrice = ParseDecimal(data.BestAsk),
            Volume24h = ParseDecimal(data.BaseVolume),
            Change24h = ParseDecimal(data.ChangeUtc24h),
            High24h = ParseDecimal(data.High24h),
            Low24h = ParseDecimal(data.Low24h),
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        var response = await GetMarketAsync<WeexResponse<List<List<string>>>>(
            "/api/v2/market/klines",
            $"?symbol={Uri.EscapeDataString(symbol)}&period={Uri.EscapeDataString(interval)}&limit={limit}",
            requiresAuth: false,
            ct);

        var data = response.Data ?? new List<List<string>>();
        return data.Select(k => new KlineData
        {
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k[0])).UtcDateTime,
            Open = ParseDecimal(k[1]),
            High = ParseDecimal(k[2]),
            Low = ParseDecimal(k[3]),
            Close = ParseDecimal(k[4]),
            Volume = ParseDecimal(k[5]),
            CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k[6])).UtcDateTime
        }).ToList();
    }

    // =========================
    //  Market Rates (Contract-only)
    // =========================

    public Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default)
    {
        Logger.LogDebug("[WeexSpot] GetCurrentFundingRateAsync is contract-only. Return null. Symbol={Symbol}", symbol);
        return Task.FromResult<decimal?>(null);
    }

    public Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
    {
        Logger.LogDebug("[WeexSpot] GetOpenInterestAsync is contract-only. Return null. Symbol={Symbol}", symbol);
        return Task.FromResult<decimal?>(null);
    }

    // =========================
    //  Account
    // =========================

    public async Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
    {
        var response = await GetTradingAsync<WeexResponse<List<BalanceDto>>>(
            "/api/v2/account/balance",
            "",
            requiresAuth: true,
            ct);

        var data = response.Data ?? new List<BalanceDto>();
        return data.Select(b => new BalanceInfo
        {
            Currency = b.Currency,
            Balance = ParseDecimal(b.Balance),
            Available = ParseDecimal(b.Available),
            Frozen = ParseDecimal(b.Frozen)
        }).ToList();
    }

    public async Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct);
        return balances.FirstOrDefault(b => b.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
    }

    // =========================
    //  Trading
    // =========================

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            symbol = request.Symbol,
            side = request.Side,
            orderType = request.OrderType,
            force = request.Force,
            quantity = request.Quantity,
            price = request.Price ?? "0",
            clientOrderId = request.ClientOrderId ?? GenerateClientOrderId()
        };

        try
        {
            var response = await PostTradingAsync<WeexResponse<OrderResultDto>>(
                "/api/v2/trade/orders",
                "",
                body,
                requiresAuth: true,
                ct);

            if (response.Code == "00000" && response.Data != null)
            {
                return new OrderResult
                {
                    Success = true,
                    OrderId = response.Data.OrderId,
                    ClientOrderId = response.Data.ClientOrderId
                };
            }

            return new OrderResult
            {
                Success = false,
                ErrorCode = response.Code,
                ErrorMessage = response.Msg
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to place spot order for {Symbol}", request.Symbol);
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
        var body = new Dictionary<string, string> { ["symbol"] = symbol };
        if (!string.IsNullOrWhiteSpace(orderId))
            body["orderId"] = orderId;
        if (!string.IsNullOrWhiteSpace(clientOrderId))
            body["clientOid"] = clientOrderId;

        try
        {
            var response = await PostTradingAsync<WeexResponse<CancelOrderDto>>(
                "/api/v2/trade/cancel-order",
                "",
                body,
                requiresAuth: true,
                ct);

            if (response.Code == "00000" && response.Data?.Result == true)
            {
                return new CancelOrderResult
                {
                    Success = true,
                    OrderId = response.Data.OrderId,
                    ClientOrderId = response.Data.ClientOid
                };
            }

            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = response.Data?.ErrCode ?? response.Code,
                ErrorMessage = response.Data?.ErrMsg ?? response.Msg
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel spot order {OrderId}", orderId ?? clientOrderId);
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
        var query = $"symbol={Uri.EscapeDataString(symbol)}";
        if (!string.IsNullOrWhiteSpace(orderId))
            query += $"&orderId={Uri.EscapeDataString(orderId)}";
        if (!string.IsNullOrWhiteSpace(clientOrderId))
            query += $"&clientOid={Uri.EscapeDataString(clientOrderId)}";

        var response = await GetTradingAsync<WeexResponse<OrderDto>>(
            "/api/v2/trade/order",
            $"?{query}",
            requiresAuth: true,
            ct);

        if (response.Data == null)
            return null;

        return MapOrderDto(response.Data);
    }

    public async Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(symbol) ? "" : $"?symbol={Uri.EscapeDataString(symbol)}";
        var response = await GetTradingAsync<WeexResponse<List<OrderDto>>>(
            "/api/v2/trade/open-orders",
            query,
            requiresAuth: true,
            ct);

        var data = response.Data ?? new List<OrderDto>();
        return data.Select(MapOrderDto).ToList();
    }

    // =========================
    //  Contract-only endpoints (Spot returns empty)
    // =========================

    public Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(string? symbol = null, CancellationToken ct = default)
    {
        Logger.LogDebug("[WeexSpot] GetPositionsAsync is contract-only. Return empty. Symbol={Symbol}", symbol);
        return Task.FromResult<IReadOnlyList<PositionInfo>>(Array.Empty<PositionInfo>());
    }

    public Task<IReadOnlyList<FillInfo>> GetFillsAsync(string? symbol = null, int limit = 50, CancellationToken ct = default)
    {
        Logger.LogDebug("[WeexSpot] GetFillsAsync is contract-only. Return empty. Symbol={Symbol}", symbol);
        return Task.FromResult<IReadOnlyList<FillInfo>>(Array.Empty<FillInfo>());
    }

    private static OrderInfo MapOrderDto(OrderDto dto) => new()
    {
        OrderId = dto.OrderId,
        ClientOrderId = dto.ClientOid,
        Symbol = dto.Symbol,
        Side = dto.Side,
        OrderType = dto.OrderType,
        Status = dto.Status,
        Price = ParseDecimal(dto.Price),
        Quantity = ParseDecimal(dto.Size),
        FilledQuantity = ParseDecimal(dto.FilledSize),
        FilledPrice = ParseDecimal(dto.FilledPrice),
        Fee = ParseDecimal(dto.Fee),
        CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreateTime).UtcDateTime,
        UpdateTime = dto.UpdateTime > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdateTime).UtcDateTime
            : null
    };

    // =========================
    //  DTOs (Spot)
    // =========================

    private record WeexResponse<T>
    {
        public string Code { get; init; } = "";
        public string Msg { get; init; } = "";
        public long RequestTime { get; init; }
        public T? Data { get; init; }
    }

    private record TickerDto
    {
        public string? Last { get; init; }
        public string? BestBid { get; init; }
        public string? BestAsk { get; init; }
        public string? BaseVolume { get; init; }
        public string? ChangeUtc24h { get; init; }
        public string? High24h { get; init; }
        public string? Low24h { get; init; }
    }

    private record BalanceDto
    {
        public string Currency { get; init; } = "";
        public string? Balance { get; init; }
        public string? Available { get; init; }
        public string? Frozen { get; init; }
    }

    private record OrderResultDto
    {
        public string? OrderId { get; init; }
        public string? ClientOrderId { get; init; }
    }

    private record CancelOrderDto
    {
        public string? OrderId { get; init; }
        public string? ClientOid { get; init; }
        public bool Result { get; init; }
        public string? ErrCode { get; init; }
        public string? ErrMsg { get; init; }
    }

    private record OrderDto
    {
        public string OrderId { get; init; } = "";
        public string? ClientOid { get; init; }
        public string Symbol { get; init; } = "";
        public string Side { get; init; } = "";
        public string OrderType { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Price { get; init; }
        public string? Size { get; init; }
        public string? FilledSize { get; init; }
        public string? FilledPrice { get; init; }
        public string? Fee { get; init; }
        public long CreateTime { get; init; }
        public long UpdateTime { get; init; }
    }
}


