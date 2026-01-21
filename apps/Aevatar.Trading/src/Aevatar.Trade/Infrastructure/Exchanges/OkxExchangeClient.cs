using Aevatar.Trade.Infrastructure.WeexApi;

namespace Aevatar.Trade.Infrastructure.Exchanges;

// ============================================================================
//  OKX 占位适配器 / OKX Placeholder
//  - 目前仅用于配置切换与能力降级，不提供真实交易所访问
// ============================================================================

public sealed class OkxExchangeClient :
    IExchangeClient,
    IExchangeMarketDataClient,
    IExchangeAccountClient,
    IExchangeTradeClient
{
    private static NotSupportedException NotSupported(string action) =>
        new($"OKX adapter not implemented: {action}");

    private readonly ExchangeCapabilities _capabilities = new()
    {
        SupportsWebSocket = false,
        SupportsPositions = false,
        SupportsFundingRate = false,
        SupportsOpenInterest = false,
        SupportsFills = false,
        SupportsBalances = false,
        SupportsOrders = false,
        SupportsKlines = false
    };

    public ExchangeCapabilities Capabilities => _capabilities;
    public IExchangeMarketDataClient MarketData => this;
    public IExchangeAccountClient Account => this;
    public IExchangeTradeClient Trade => this;
    public IExchangeMarketStreamClient? MarketStream => null;

    // ============ Market Data ============

    public Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
        => throw NotSupported(nameof(GetTickerAsync));

    public Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
        => throw NotSupported(nameof(GetKlinesAsync));

    public Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default)
        => throw NotSupported(nameof(GetCurrentFundingRateAsync));

    public Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
        => throw NotSupported(nameof(GetOpenInterestAsync));

    // ============ Account ============

    public Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
        => throw NotSupported(nameof(GetBalancesAsync));

    public Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
        => throw NotSupported(nameof(GetBalanceAsync));

    public Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(
        string? symbol = null,
        CancellationToken ct = default)
        => throw NotSupported(nameof(GetPositionsAsync));

    public Task<IReadOnlyList<FillInfo>> GetFillsAsync(
        string? symbol = null,
        int limit = 50,
        CancellationToken ct = default)
        => throw NotSupported(nameof(GetFillsAsync));

    // ============ Trading ============

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        => throw NotSupported(nameof(PlaceOrderAsync));

    public Task<CancelOrderResult> CancelOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
        => throw NotSupported(nameof(CancelOrderAsync));

    public Task<OrderInfo?> GetOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
        => throw NotSupported(nameof(GetOrderAsync));

    public Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null,
        CancellationToken ct = default)
        => throw NotSupported(nameof(GetOpenOrdersAsync));
}
