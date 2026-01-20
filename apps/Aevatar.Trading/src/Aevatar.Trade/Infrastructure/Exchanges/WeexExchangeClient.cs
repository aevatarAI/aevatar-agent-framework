using Aevatar.Trade.Infrastructure.WeexApi;

namespace Aevatar.Trade.Infrastructure.Exchanges;

// ============================================================================
//  WEEX 适配器 / Weex Exchange Adapter
//  - 复用现有 IWeexApiClient 与 WeexWebSocketClient
// ============================================================================

public sealed class WeexExchangeClient :
    IExchangeClient,
    IExchangeMarketDataClient,
    IExchangeAccountClient,
    IExchangeTradeClient
{
    private readonly IWeexApiClient _apiClient;
    private readonly IExchangeMarketStreamClient _marketStream;
    private readonly ExchangeCapabilities _capabilities = new()
    {
        SupportsWebSocket = true,
        SupportsPositions = true,
        SupportsFundingRate = true,
        SupportsOpenInterest = true,
        SupportsFills = true,
        SupportsBalances = true,
        SupportsOrders = true,
        SupportsKlines = true
    };

    public WeexExchangeClient(IWeexApiClient apiClient, WeexWebSocketClient wsClient)
    {
        _apiClient = apiClient;
        _marketStream = new WeexMarketStreamAdapter(wsClient);
    }

    public ExchangeCapabilities Capabilities => _capabilities;
    public IExchangeMarketDataClient MarketData => this;
    public IExchangeAccountClient Account => this;
    public IExchangeTradeClient Trade => this;
    public IExchangeMarketStreamClient? MarketStream => _marketStream;

    // ============ Market Data ============

    public Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
        => _apiClient.GetTickerAsync(symbol, ct);

    public Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
        => _apiClient.GetKlinesAsync(symbol, interval, limit, ct);

    public Task<decimal?> GetCurrentFundingRateAsync(string symbol, CancellationToken ct = default)
        => _apiClient.GetCurrentFundingRateAsync(symbol, ct);

    public Task<decimal?> GetOpenInterestAsync(string symbol, CancellationToken ct = default)
        => _apiClient.GetOpenInterestAsync(symbol, ct);

    // ============ Account ============

    public Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
        => _apiClient.GetBalancesAsync(ct);

    public Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
        => _apiClient.GetBalanceAsync(currency, ct);

    public Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(
        string? symbol = null,
        CancellationToken ct = default)
        => _apiClient.GetPositionsAsync(symbol, ct);

    public Task<IReadOnlyList<FillInfo>> GetFillsAsync(
        string? symbol = null,
        int limit = 50,
        CancellationToken ct = default)
        => _apiClient.GetFillsAsync(symbol, limit, ct);

    // ============ Trading ============

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
        => _apiClient.PlaceOrderAsync(request, ct);

    public Task<CancelOrderResult> CancelOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
        => _apiClient.CancelOrderAsync(symbol, orderId, clientOrderId, ct);

    public Task<OrderInfo?> GetOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
        => _apiClient.GetOrderAsync(symbol, orderId, clientOrderId, ct);

    public Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null,
        CancellationToken ct = default)
        => _apiClient.GetOpenOrdersAsync(symbol, ct);

    // ============================================================================
    //  内部适配：把 WeexWebSocketClient 映射到 IExchangeMarketStreamClient
    // ============================================================================
    private sealed class WeexMarketStreamAdapter : IExchangeMarketStreamClient
    {
        private readonly WeexWebSocketClient _inner;

        public WeexMarketStreamAdapter(WeexWebSocketClient inner)
        {
            _inner = inner;
            _inner.OnTicker += evt => OnTicker?.Invoke(evt);
            _inner.OnKline += evt => OnKline?.Invoke(evt);
            _inner.OnError += msg => OnError?.Invoke(msg);
            _inner.OnConnected += () => OnConnected?.Invoke();
            _inner.OnDisconnected += () => OnDisconnected?.Invoke();
        }

        public bool IsConnected => _inner.IsConnected;
        public event Action<TickerResponse>? OnTicker;
        public event Action<KlineData>? OnKline;
        public event Action<string>? OnError;
        public event Action? OnConnected;
        public event Action? OnDisconnected;

        public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
        public Task DisconnectAsync() => _inner.DisconnectAsync();
        public Task SubscribeTickerAsync(string symbol, CancellationToken ct = default) => _inner.SubscribeTickerAsync(symbol, ct);
        public Task SubscribeKlineAsync(string symbol, string interval, CancellationToken ct = default) => _inner.SubscribeKlineAsync(symbol, interval, ct);
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
