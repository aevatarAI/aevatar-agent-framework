using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

/// <summary>
/// WEEX WebSocket client
/// Used for subscribing to real-time market data
/// Official documentation: https://www.weex.com/api-doc/spot/Websocket/public/Tickers-Channel
/// </summary>
public class WeexWebSocketClient : IAsyncDisposable
{
    private readonly WeexApiConfig _config;
    private readonly ILogger<WeexWebSocketClient> _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    // ============ Events ============
    
    public event Action<TickerResponse>? OnTicker;
    public event Action<KlineData>? OnKline;
    public event Action<string>? OnError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public WeexWebSocketClient(
        IOptions<WeexApiConfig> config,
        ILogger<WeexWebSocketClient> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    // ============ Connection Management ============

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _webSocket = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // ============================================================
        //  WEEX WebSocket Endpoint
        //
        //  - Default: derive from REST BaseUrl (BaseUrl -> ws(s) + /ws/public)
        //  - Override: set Weex:PublicWebSocketUrl in configuration if WEEX uses a different host/path
        //
        //  Many WS gateways also enforce an Origin header; we set a safe default.
        // ============================================================
        _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _webSocket.Options.SetRequestHeader("User-Agent", "Aevatar.Trade/1.0");
        if (!string.IsNullOrWhiteSpace(_config.WebSocketOrigin))
        {
            _webSocket.Options.SetRequestHeader("Origin", _config.WebSocketOrigin);
        }

        var wsUrl = !string.IsNullOrWhiteSpace(_config.PublicWebSocketUrl)
            ? _config.PublicWebSocketUrl
            : DerivePublicWsUrl(GetWebSocketBaseUrl());

        _logger.LogInformation("Connecting to WEEX WebSocket: {Url}", wsUrl);
        try
        {
            await _webSocket.ConnectAsync(new Uri(wsUrl), ct);
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex,
                "WEEX WebSocket connect failed. Url={Url}, BaseUrl={BaseUrl}. " +
                "Hint: If you see 403/101 errors, configure 'Weex:PublicWebSocketUrl' (and optionally 'Weex:WebSocketOrigin').",
                wsUrl, _config.BaseUrl);
            throw;
        }

        _logger.LogInformation("Connected to WEEX WebSocket");
        OnConnected?.Invoke();

        // Start receive loop
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    private static string DerivePublicWsUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "https://api-spot.weex.com";
        }

        trimmed = trimmed
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);

        return $"{trimmed}/ws/public";
    }

    private string GetWebSocketBaseUrl()
    {
        // Prefer market-data base url for websocket (spot public channels).
        // If user switches trading base to api-contract (AI Wars), we should not derive WS from it.
        var market = (_config.MarketDataBaseUrl ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(market))
        {
            return market;
        }

        return _config.BaseUrl;
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket == null) return;

        _cts?.Cancel();

        if (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during WebSocket close");
            }
        }

        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch { /* ignore */ }
        }

        OnDisconnected?.Invoke();
        _logger.LogInformation("Disconnected from WEEX WebSocket");
    }

    // ============ Subscriptions ============

    /// <summary>
    /// Subscribe to ticker
    /// </summary>
    public async Task SubscribeTickerAsync(string symbol, CancellationToken ct = default)
    {
        var message = new
        {
            @event = "subscribe",
            channel = $"ticker.{symbol}"
        };

        await SendAsync(message, ct);
        _logger.LogDebug("Subscribed to ticker: {Symbol}", symbol);
    }

    /// <summary>
    /// Subscribe to kline
    /// </summary>
    public async Task SubscribeKlineAsync(string symbol, string interval, CancellationToken ct = default)
    {
        var message = new
        {
            @event = "subscribe",
            channel = $"kline.{interval}.{symbol}"
        };

        await SendAsync(message, ct);
        _logger.LogDebug("Subscribed to kline: {Symbol} {Interval}", symbol, interval);
    }

    /// <summary>
    /// Unsubscribe
    /// </summary>
    public async Task UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        var message = new
        {
            @event = "unsubscribe",
            channel
        };

        await SendAsync(message, ct);
        _logger.LogDebug("Unsubscribed from: {Channel}", channel);
    }

    // ============ Private Methods ============

    private async Task SendAsync(object message, CancellationToken ct)
    {
        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket!.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                var result = await _webSocket!.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WebSocket receive loop");
            OnError?.Invoke(ex.Message);
        }
        finally
        {
            OnDisconnected?.Invoke();
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Handle ping/pong
            if (root.TryGetProperty("event", out var eventProp))
            {
                var eventType = eventProp.GetString();
                if (eventType == "ping")
                {
                    _ = SendPongAsync();
                    return;
                }
            }

            // Handle data push
            if (root.TryGetProperty("channel", out var channelProp))
            {
                var channel = channelProp.GetString() ?? "";

                if (channel.StartsWith("ticker."))
                {
                    ProcessTickerMessage(root);
                }
                else if (channel.StartsWith("kline."))
                {
                    ProcessKlineMessage(root);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process WebSocket message: {Message}", message);
        }
    }

    private void ProcessTickerMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return;

        var ticker = new TickerResponse
        {
            Symbol = data.GetProperty("symbol").GetString() ?? "",
            LastPrice = ParseDecimal(data, "last"),
            BidPrice = ParseDecimal(data, "bestBid"),
            AskPrice = ParseDecimal(data, "bestAsk"),
            Volume24h = ParseDecimal(data, "baseVolume"),
            Change24h = ParseDecimal(data, "changeUtc24h"),
            High24h = ParseDecimal(data, "high24h"),
            Low24h = ParseDecimal(data, "low24h"),
            Timestamp = DateTime.UtcNow
        };

        OnTicker?.Invoke(ticker);
    }

    private void ProcessKlineMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return;

        var kline = new KlineData
        {
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(
                data.GetProperty("t").GetInt64()).UtcDateTime,
            Open = ParseDecimal(data, "o"),
            High = ParseDecimal(data, "h"),
            Low = ParseDecimal(data, "l"),
            Close = ParseDecimal(data, "c"),
            Volume = ParseDecimal(data, "v"),
            CloseTime = DateTime.UtcNow
        };

        OnKline?.Invoke(kline);
    }

    private async Task SendPongAsync()
    {
        try
        {
            await SendAsync(new { @event = "pong" }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send pong");
        }
    }

    private static decimal ParseDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return 0m;
        
        return prop.ValueKind switch
        {
            JsonValueKind.String => decimal.TryParse(prop.GetString(), out var d) ? d : 0m,
            JsonValueKind.Number => prop.GetDecimal(),
            _ => 0m
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
