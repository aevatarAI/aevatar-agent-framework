using Aevatar.Trade.Infrastructure.WeexApi;

namespace Aevatar.Trade.Infrastructure.Exchanges;

// ============================================================================
//  交易所抽象接口 / Exchange Abstractions
//  - 仅暴露通用能力，避免把业务绑定到单一交易所
// ============================================================================

public interface IExchangeClient
{
    ExchangeCapabilities Capabilities { get; }
    IExchangeMarketDataClient MarketData { get; }
    IExchangeAccountClient Account { get; }
    IExchangeTradeClient Trade { get; }
    IExchangeMarketStreamClient? MarketStream { get; }
}

// ============ Market Data ============

public interface IExchangeMarketDataClient
{
    Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default);

    Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default);
    Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default);
}

public interface IExchangeMarketStreamClient : IAsyncDisposable
{
    bool IsConnected { get; }
    event Action<TickerResponse>? OnTicker;
    event Action<KlineData>? OnKline;
    event Action<string>? OnError;
    event Action? OnConnected;
    event Action? OnDisconnected;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task SubscribeTickerAsync(string symbol, CancellationToken ct = default);
    Task SubscribeKlineAsync(string symbol, string interval, CancellationToken ct = default);
}

// ============ Account ============

public interface IExchangeAccountClient
{
    Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default);
    Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default);
    Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(
        string? symbol = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<FillInfo>> GetFillsAsync(
        string? symbol = null,
        int limit = 50,
        CancellationToken ct = default);
}

// ============ Trading ============

public interface IExchangeTradeClient
{
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    Task<CancelOrderResult> CancelOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default);
    Task<OrderInfo?> GetOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null,
        CancellationToken ct = default);
}
