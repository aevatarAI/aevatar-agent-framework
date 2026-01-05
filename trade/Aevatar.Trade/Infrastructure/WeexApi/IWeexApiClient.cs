namespace Aevatar.Trade.Infrastructure.WeexApi;

/// <summary>
/// WEEX API client interface
/// AI Wars (Contract) docs: https://www.weex.com/api-doc/ai/intro
/// Spot docs: https://www.weex.com/api-doc/spot/introduction/APIBriefIntroduction
/// </summary>
public interface IWeexApiClient
{
    // ============ Market Data ============
    
    /// <summary>
    /// Get ticker for a single trading pair
    /// </summary>
    Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default);
    
    /// <summary>
    /// Get kline data
    /// </summary>
    /// <param name="symbol">Trading pair (e.g., "cmt_btcusdt")</param>
    /// <param name="interval">Time interval (AI Wars: 1m, 5m, 15m, 30m, 1h, 4h, 12h, 1d, 1w)</param>
    /// <param name="limit">Quantity limit</param>
    Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol, 
        string interval, 
        int limit = 100, 
        CancellationToken ct = default);

    // ============ Market Rates (Contract-only; Spot returns null) ============

    /// <summary>
    /// Get current funding rate for a contract symbol.
    /// Contract API: GET /capi/v2/market/currentFundRate
    ///
    /// NOTE:
    /// - Return value is **ratio** (e.g., 0.0001 means 0.01%).
    /// - Spot mode returns null.
    /// </summary>
    Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Get open interest for a contract symbol.
    /// Contract API: GET /capi/v2/market/open_interest
    ///
    /// NOTE:
    /// - Unit is exchange-defined; we pass through the API numeric value.
    /// - Spot mode returns null.
    /// </summary>
    Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default);
    
    // ============ Account ============
    
    /// <summary>
    /// Get account balance
    /// GET /api/v2/account/balance
    /// </summary>
    Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Get balance for a specific currency
    /// </summary>
    Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default);
    
    // ============ Trading ============
    
    /// <summary>
    /// Place order
    /// POST /api/v2/trade/orders
    /// </summary>
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Cancel order
    /// POST /api/v2/trade/cancel-order
    /// </summary>
    Task<CancelOrderResult> CancelOrderAsync(
        string symbol, 
        string? orderId = null, 
        string? clientOrderId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get order details
    /// </summary>
    Task<OrderInfo?> GetOrderAsync(
        string symbol, 
        string? orderId = null, 
        string? clientOrderId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get current pending orders
    /// </summary>
    Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null, 
        CancellationToken ct = default);

    // ============ Contract-only (positions / fills) ============

    /// <summary>
    /// Get positions (contract accounts only)
    /// GET /capi/v2/account/position/allPosition
    /// </summary>
    Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(
        string? symbol = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get fills / trade details (contract accounts only)
    /// GET /capi/v2/order/fills
    /// </summary>
    Task<IReadOnlyList<FillInfo>> GetFillsAsync(
        string? symbol = null,
        int limit = 50,
        CancellationToken ct = default);
}

// ============ Request/Response Models ============

/// <summary>
/// Order request
/// </summary>
public record OrderRequest
{
    /// <summary>Trading pair (e.g., "cmt_btcusdt")</summary>
    public required string Symbol { get; init; }
    
    /// <summary>Direction: buy, sell</summary>
    public required string Side { get; init; }
    
    /// <summary>Order type: limit, market</summary>
    public required string OrderType { get; init; }
    
    /// <summary>Execution strategy: normal, postOnly, fok, ioc</summary>
    public string Force { get; init; } = "normal";
    
    /// <summary>Quantity</summary>
    public required string Quantity { get; init; }
    
    /// <summary>Price (required for limit orders)</summary>
    public string? Price { get; init; }
    
    /// <summary>Client order ID</summary>
    public string? ClientOrderId { get; init; }
}

/// <summary>
/// Order result
/// </summary>
public record OrderResult
{
    public bool Success { get; init; }
    public string? OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Cancel order result
/// </summary>
public record CancelOrderResult
{
    public bool Success { get; init; }
    public string? OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Order information
/// </summary>
public record OrderInfo
{
    public required string OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public required string Status { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal FilledPrice { get; init; }
    public decimal Fee { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime? UpdateTime { get; init; }
}

/// <summary>
/// Balance information
/// </summary>
public record BalanceInfo
{
    public required string Currency { get; init; }
    public decimal Balance { get; init; }
    public decimal Available { get; init; }
    public decimal Frozen { get; init; }
}

/// <summary>
/// Ticker data
/// </summary>
public record TickerResponse
{
    public required string Symbol { get; init; }
    public decimal LastPrice { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public decimal Volume24h { get; init; }
    public decimal Change24h { get; init; }
    public decimal High24h { get; init; }
    public decimal Low24h { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Kline data
/// </summary>
public record KlineData
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public DateTime CloseTime { get; init; }
}

/// <summary>
/// Contract position snapshot (normalized; may contain nulls if API omits fields)
/// </summary>
public record PositionInfo
{
    public required string Symbol { get; init; }
    public string Side { get; init; } = "UNKNOWN"; // LONG / SHORT / BUY / SELL / UNKNOWN (best-effort)
    public decimal Size { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? MarkPrice { get; init; }
    public decimal? UnrealizedPnl { get; init; }
    public decimal? Notional { get; init; }
    public decimal? Leverage { get; init; }
}

/// <summary>
/// Contract fill / execution detail (normalized; may contain nulls if API omits fields)
/// </summary>
public record FillInfo
{
    /// <summary>
    /// Unix millisecond timestamp (best-effort; useful for charts)
    /// </summary>
    public long? Ts { get; init; }

    public DateTime? TimeUtc { get; init; }
    public string? Symbol { get; init; }
    public string Side { get; init; } = "UNKNOWN"; // BUY / SELL / LONG / SHORT / UNKNOWN (best-effort)
    public decimal? Price { get; init; }
    public decimal? Quantity { get; init; }
    public string? OrderId { get; init; }
    public decimal? Fee { get; init; }
}
