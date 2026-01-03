using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Threading;

namespace Aevatar.Trade.Agents.Data;

/// <summary>
/// Data collection agent
/// Responsibilities: Connect to WEEX API, collect market data, convert to internal events and broadcast to downstream agents
/// </summary>
public class DataCollectorAgent : GAgentBase<DataCollectorState>
{
    // ============ Dependencies ============
    
    private IWeexApiClient? _apiClient;
    private WeexWebSocketClient? _wsClient;
    private readonly List<string> _subscribedSymbols = new();
    private Timer? _heartbeatTimer;
    private Timer? _pollingTimer;
    private int _pollingRunning;
    private DateTime _nextKlinePollUtc = DateTime.MinValue;
    private string _pollingInterval = "15m";

    public IWeexApiClient ApiClient
    {
        set => _apiClient = value;
    }

    public WeexWebSocketClient WebSocketClient
    {
        set
        {
            _wsClient = value;
            SetupWebSocketHandlers();
        }
    }

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        State.AgentId = Id.ToString();
        State.IsConnected = false;
        State.TicksReceived = 0;

        Logger.LogInformation("[DataCollector] Agent activated: {AgentId}", State.AgentId);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        _heartbeatTimer?.Dispose();
        _pollingTimer?.Dispose();
        
        if (_wsClient != null)
        {
            await _wsClient.DisconnectAsync();
        }

        await base.OnDeactivateAsync(ct);
    }

    public override Task<string> GetDescriptionAsync()
    {
        // NOTE:
        // - "Connected" historically meant WebSocket connected.
        // - In AI Wars / contract environment, WS may be unavailable; REST polling is the normal/expected path.
        var mode = State.IsConnected
            ? "Mode=WebSocket"
            : _pollingTimer != null
                ? "Mode=REST polling"
                : "Mode=Stopped";

        return Task.FromResult(
            $"DataCollector: {_subscribedSymbols.Count} symbols, " +
            $"{State.TicksReceived} ticks, " +
            mode);
    }

    // ============ Public Methods ============

    /// <summary>
    /// Start data collection
    /// </summary>
    public async Task StartCollectingAsync(
        IEnumerable<string> symbols,
        string klineInterval = "15m",
        CancellationToken ct = default)
    {
        if (_wsClient == null)
            throw new InvalidOperationException("WebSocket client not configured");

        _pollingInterval = string.IsNullOrWhiteSpace(klineInterval) ? "15m" : klineInterval.Trim();

        // Connect WebSocket (preferred). If handshake fails (403/521 etc), fall back to REST polling so demos don't hard-fail.
        try
        {
            await _wsClient.ConnectAsync(ct);
            State.IsConnected = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            State.IsConnected = false;
            Logger.LogWarning(ex,
                "[DataCollector] WebSocket connect failed; falling back to REST polling. Interval={Interval}",
                _pollingInterval);
            StartPolling(symbols);
        }

        // Subscribe to market data
        foreach (var symbol in symbols)
        {
            if (State.IsConnected)
            {
                await _wsClient.SubscribeTickerAsync(symbol, ct);
                await _wsClient.SubscribeKlineAsync(symbol, _pollingInterval, ct);
            }
            
            _subscribedSymbols.Add(symbol);
            State.SubscribedSymbols.Add(symbol);
        }

        // ============================================================
        //  Cold start: Seed historical klines ASAP
        //
        //  Why:
        //  - TechnicalAnalystAgent requires a buffer (default 60 klines).
        //  - If we only poll 1 kline every 15m, the first technical analysis could take ~15 hours.
        //
        //  Strategy:
        //  - Fetch recent klines once at start (e.g. 120) and publish as KlineUpdateEvent.
        //  - Keep it best-effort (no hard fail). If API is rate-limited, we still run with WS/ticks.
        // ============================================================
        if (_apiClient != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var symbol in _subscribedSymbols)
                    {
                        await FetchHistoricalKlinesAsync(symbol, _pollingInterval, limit: 120, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogWarning(ex, "[DataCollector] Historical klines seeding failed (non-fatal)");
                }
            }, ct);
        }

        // Start heartbeat
        _heartbeatTimer = new Timer(
            _ => CheckConnection(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        Logger.LogInformation(
            "[DataCollector] Started collecting for {Count} symbols: {Symbols}",
            _subscribedSymbols.Count,
            string.Join(", ", _subscribedSymbols));

        // Publish system started event
        await PublishAsync(new SystemStartedEvent
        {
            SystemId = State.AgentId,
            ActiveSymbols = { _subscribedSymbols },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    /// <summary>
    /// Stop data collection
    /// </summary>
    public async Task StopCollectingAsync(string reason = "Manual stop")
    {
        if (_wsClient != null)
        {
            await _wsClient.DisconnectAsync();
        }

        State.IsConnected = false;
        _heartbeatTimer?.Dispose();
        _pollingTimer?.Dispose();

        Logger.LogInformation("[DataCollector] Stopped collecting: {Reason}", reason);

        await PublishAsync(new SystemStoppedEvent
        {
            SystemId = State.AgentId,
            Reason = reason,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    /// <summary>
    /// Manually fetch historical klines
    /// </summary>
    public async Task FetchHistoricalKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (_apiClient == null)
            throw new InvalidOperationException("API client not configured");

        var klines = await _apiClient.GetKlinesAsync(symbol, interval, limit, ct);

        // Ensure chronological order (old -> new) for indicators.
        foreach (var kline in klines.OrderBy(k => k.OpenTime))
        {
            await PublishAsync(new KlineUpdateEvent
            {
                Symbol = symbol,
                Interval = interval,
                Open = (double)kline.Open,
                High = (double)kline.High,
                Low = (double)kline.Low,
                Close = (double)kline.Close,
                Volume = (double)kline.Volume,
                OpenTime = Timestamp.FromDateTime(kline.OpenTime),
                CloseTime = Timestamp.FromDateTime(kline.CloseTime)
            });
        }

        Logger.LogDebug(
            "[DataCollector] Fetched {Count} historical klines for {Symbol}",
            klines.Count, symbol);
    }

    // ============ WebSocket Handlers ============

    private void SetupWebSocketHandlers()
    {
        if (_wsClient == null) return;

        _wsClient.OnTicker += OnTickerReceived;
        _wsClient.OnKline += OnKlineReceived;
        _wsClient.OnConnected += OnWebSocketConnected;
        _wsClient.OnDisconnected += OnWebSocketDisconnected;
        _wsClient.OnError += OnWebSocketError;
    }

    // ============ REST Polling Fallback ============

    private void StartPolling(IEnumerable<string> symbols)
    {
        if (_apiClient == null)
        {
            Logger.LogWarning("[DataCollector] REST polling fallback skipped: API client not configured.");
            return;
        }

        // Initialize kline poll schedule (try to avoid hammering the API).
        _nextKlinePollUtc = DateTime.UtcNow;

        // Poll tickers frequently; klines at (roughly) kline interval.
        _pollingTimer?.Dispose();
        // 高频 tick：用户明确要求“决策更频繁”，这里把 REST polling 从 2s 提升到 1s。
        // NOTE: 仍有 _pollingRunning 互斥保护，避免 HTTP 调用慢时重入堆积。
        _pollingTimer = new Timer(_ => _ = PollOnceAsync(symbols), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private async Task PollOnceAsync(IEnumerable<string> symbols)
    {
        if (_apiClient == null) return;
        if (Interlocked.Exchange(ref _pollingRunning, 1) == 1) return;

        try
        {
            foreach (var symbol in symbols)
            {
                var ticker = await _apiClient.GetTickerAsync(symbol);
                OnTickerReceived(ticker);
            }

            if (DateTime.UtcNow >= _nextKlinePollUtc)
            {
                var next = DateTime.UtcNow + ParseIntervalToTimeSpan(_pollingInterval);
                _nextKlinePollUtc = next;

                foreach (var symbol in symbols)
                {
                    var klines = await _apiClient.GetKlinesAsync(symbol, _pollingInterval, limit: 1);
                    var last = klines.LastOrDefault();
                    if (last == null) continue;

                    var evt = new KlineUpdateEvent
                    {
                        Symbol = symbol,
                        Interval = _pollingInterval,
                        Open = (double)last.Open,
                        High = (double)last.High,
                        Low = (double)last.Low,
                        Close = (double)last.Close,
                        Volume = (double)last.Volume,
                        OpenTime = Timestamp.FromDateTime(last.OpenTime),
                        CloseTime = Timestamp.FromDateTime(last.CloseTime)
                    };

                    await PublishKlineEventAsync(evt);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[DataCollector] REST polling failed");
        }
        finally
        {
            Interlocked.Exchange(ref _pollingRunning, 0);
        }
    }

    private static TimeSpan ParseIntervalToTimeSpan(string interval)
    {
        // Supported shapes: "1m", "5m", "15m", "1h", "4h", "1d"
        if (string.IsNullOrWhiteSpace(interval)) return TimeSpan.FromMinutes(15);
        interval = interval.Trim();

        var unit = interval[^1];
        if (!int.TryParse(interval[..^1], out var value) || value <= 0) return TimeSpan.FromMinutes(15);

        return unit switch
        {
            'm' or 'M' => TimeSpan.FromMinutes(value),
            'h' or 'H' => TimeSpan.FromHours(value),
            'd' or 'D' => TimeSpan.FromDays(value),
            _ => TimeSpan.FromMinutes(15)
        };
    }

    private void OnTickerReceived(TickerResponse ticker)
    {
        State.TicksReceived++;
        State.LatestPrices[ticker.Symbol] = (double)ticker.LastPrice;
        State.LastTickTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // Convert to internal event and broadcast
        var evt = new MarketTickEvent
        {
            Symbol = ticker.Symbol,
            Price = (double)ticker.LastPrice,
            Bid = (double)ticker.BidPrice,
            Ask = (double)ticker.AskPrice,
            Volume24H = (double)ticker.Volume24h,
            Change24H = (double)ticker.Change24h,
            High24H = (double)ticker.High24h,
            Low24H = (double)ticker.Low24h,
            Timestamp = Timestamp.FromDateTime(ticker.Timestamp)
        };

        // Fire and forget - do not block WebSocket reception
        _ = PublishTickerEventAsync(evt);
    }

    private async Task PublishTickerEventAsync(MarketTickEvent evt)
    {
        try
        {
            await PublishAsync(evt);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Failed to publish ticker event");
        }
    }

    private void OnKlineReceived(KlineData kline)
    {
        var evt = new KlineUpdateEvent
        {
            // NOTE:
            // - Current WS callback does not provide channel/symbol, so we do a best-effort mapping:
            //   if only 1 symbol is subscribed, bind it; otherwise leave empty.
            // - For multi-symbol WS support, update WeexWebSocketClient to emit (symbol, interval, kline).
            Symbol = _subscribedSymbols.Count == 1 ? _subscribedSymbols[0] : "",
            Interval = string.IsNullOrWhiteSpace(_pollingInterval) ? "" : _pollingInterval,
            Open = (double)kline.Open,
            High = (double)kline.High,
            Low = (double)kline.Low,
            Close = (double)kline.Close,
            Volume = (double)kline.Volume,
            OpenTime = Timestamp.FromDateTime(kline.OpenTime),
            CloseTime = Timestamp.FromDateTime(kline.CloseTime)
        };

        _ = PublishKlineEventAsync(evt);
    }

    private async Task PublishKlineEventAsync(KlineUpdateEvent evt)
    {
        try
        {
            await PublishAsync(evt);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Failed to publish kline event");
        }
    }

    private void OnWebSocketConnected()
    {
        State.IsConnected = true;
        Logger.LogInformation("[DataCollector] WebSocket connected");
    }

    private void OnWebSocketDisconnected()
    {
        State.IsConnected = false;
        Logger.LogWarning("[DataCollector] WebSocket disconnected");

        // Attempt to reconnect
        _ = TryReconnectAsync();
    }

    private void OnWebSocketError(string error)
    {
        Logger.LogError("[DataCollector] WebSocket error: {Error}", error);
    }

    // ============ Private Methods ============

    private void CheckConnection()
    {
        if (!State.IsConnected && _wsClient != null)
        {
            Logger.LogWarning("[DataCollector] Connection lost, attempting reconnect...");
            _ = TryReconnectAsync();
        }
    }

    private async Task TryReconnectAsync()
    {
        if (_wsClient == null) return;

        try
        {
            await _wsClient.ConnectAsync();

            // Resubscribe
            foreach (var symbol in _subscribedSymbols)
            {
                await _wsClient.SubscribeTickerAsync(symbol);
            }

            State.IsConnected = true;
            Logger.LogInformation("[DataCollector] Reconnected successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Reconnection failed");
        }
    }
}
