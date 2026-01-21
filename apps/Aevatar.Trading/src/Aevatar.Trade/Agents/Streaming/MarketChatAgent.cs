using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Trade.AgUi;
using Aevatar.Trade.Infrastructure.Exchanges;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text;
using System.Linq;
using System.Threading;

namespace Aevatar.Trade.Agents.Streaming;

/// <summary>
/// 市场对话流：每 N 分钟强制输出一次“行情分析聊天”
/// </summary>
public sealed class MarketChatAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are a professional crypto market analyst.
        Your task is to provide a concise, human-readable market update every time you are asked.
        Output should be short, structured, and suitable for UI streaming.
        """;

    // ============ Dependencies ============

    public ITradeAgUiStreamSink? StreamSink { get; set; }
    public IExchangeMarketDataClient? MarketDataClient { get; set; }
    public IExchangeAccountClient? AccountClient { get; set; }
    public string DefaultSymbol { get; set; } = "";
    public IReadOnlyList<string> Symbols { get; set; } = Array.Empty<string>();

    // ============ State (in-memory) ============

    private readonly Dictionary<string, MarketTickEvent> _latestTicks = new(StringComparer.OrdinalIgnoreCase);
    private MarketSentimentAnalysisEvent? _latestSentiment;
    private TechnicalAnalysisEvent? _latestTechnical;
    private NewsImpactAnalysisEvent? _latestNews;

    private MarketChatConfig _config = new();
    private Timer? _chatTimer;
    private CancellationTokenSource? _lifetimeCts;
    private DateTime _lastChatUtc = DateTime.MinValue;
    private DateTime _lastTickUtc = DateTime.MinValue;
    private int _chatRunning;
    private int _chatsEmitted;
    private bool _warnedMissingSink;

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _lifetimeCts = new CancellationTokenSource();
        ResetChatTimer();
        Logger.LogInformation("[MarketChat] Activated: {AgentId}", Id);
    }

    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        try { _lifetimeCts?.Cancel(); } catch { /* ignore */ }
        _chatTimer?.Dispose();
        return base.OnDeactivateAsync(ct);
    }

    public override Task<string> GetDescriptionAsync()
    {
        var last = _lastChatUtc == DateTime.MinValue ? "-" : _lastChatUtc.ToString("O");
        return Task.FromResult($"MarketChat: chats={_chatsEmitted}, last={last}");
    }

    // ============ Configuration ============

    public void Configure(MarketChatConfig config)
    {
        _config = config?.Clone() ?? new MarketChatConfig();
        ResetChatTimer();
    }

    public async Task TriggerNowAsync(string reason, CancellationToken ct = default)
    {
        if (_config.ChatIntervalSeconds <= 0)
            return;

        if (!_isInitialized)
            return;

        if (Interlocked.Exchange(ref _chatRunning, 1) == 1)
            return;

        try
        {
            _lastChatUtc = DateTime.UtcNow;
            await EmitChatAsync(ct, forceReason: reason);
        }
        finally
        {
            Interlocked.Exchange(ref _chatRunning, 0);
        }
    }

    // ============ Event Handlers ============

    [EventHandler]
    public Task HandleMarketTick(MarketTickEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Symbol) || evt.Price <= 0)
            return Task.CompletedTask;

        _latestTicks[evt.Symbol] = evt;
        _lastTickUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleSentiment(MarketSentimentAnalysisEvent evt)
    {
        _latestSentiment = evt;
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleTechnical(TechnicalAnalysisEvent evt)
    {
        _latestTechnical = evt;
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleNews(NewsImpactAnalysisEvent evt)
    {
        _latestNews = evt;
        return Task.CompletedTask;
    }

    [EventHandler]
    public async Task HandleUserChat(UserChatMessageEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Content))
            return;

        if (!_isInitialized)
            return;

        if (StreamSink == null)
        {
            if (!_warnedMissingSink)
            {
                _warnedMissingSink = true;
                Logger.LogWarning("[MarketChat] StreamSink missing; AG-UI output disabled.");
            }
            return;
        }

        var content = evt.Content.Trim();
        if (content.Length > 1000)
            content = content[..1000];

        await EmitUserMessageAsync(evt.MessageId, content, CancellationToken.None);

        if (Interlocked.Exchange(ref _chatRunning, 1) == 1)
        {
            await EmitSystemMessageAsync("AI 正在处理中，请稍后再试。", CancellationToken.None);
            return;
        }

        _lastChatUtc = DateTime.UtcNow;

        try
        {
            await EmitChatAsync(CancellationToken.None, forceReason: "USER", userMessage: content);
        }
        finally
        {
            Interlocked.Exchange(ref _chatRunning, 0);
        }
    }

    // ============ Timer ============

    private int EffectiveChatIntervalSeconds()
    {
        return _config.ChatIntervalSeconds <= 0 ? 0 : _config.ChatIntervalSeconds;
    }

    private void ResetChatTimer()
    {
        _chatTimer?.Dispose();
        var intervalSeconds = EffectiveChatIntervalSeconds();
        if (intervalSeconds <= 0)
            return;

        var due = TimeSpan.FromSeconds(Math.Min(10, intervalSeconds));
        _chatTimer = new Timer(
            _ => _ = RunScheduledChatAsync(_lifetimeCts?.Token ?? CancellationToken.None),
            null,
            due,
            TimeSpan.FromSeconds(intervalSeconds));
    }

    private async Task RunScheduledChatAsync(CancellationToken ct)
    {
        var intervalSeconds = EffectiveChatIntervalSeconds();
        if (intervalSeconds <= 0)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastChatUtc).TotalSeconds < intervalSeconds)
            return;

        if (!_isInitialized)
            return;

        if (Interlocked.Exchange(ref _chatRunning, 1) == 1)
            return;

        _lastChatUtc = now;

        try
        {
            await EmitChatAsync(ct, forceReason: null);
        }
        finally
        {
            Interlocked.Exchange(ref _chatRunning, 0);
        }
    }

    // ============ Chat Streaming ============

    private async Task EmitChatAsync(CancellationToken ct, string? forceReason, string? userMessage = null)
    {
        if (StreamSink == null)
        {
            if (!_warnedMissingSink)
            {
                _warnedMissingSink = true;
                Logger.LogWarning("[MarketChat] StreamSink missing; AG-UI output disabled.");
            }
            return;
        }

        // ------------------------------------------------------------
        //  Snapshot: attempt REST ticker + positions before chat
        // ------------------------------------------------------------
        var snapshot = await CollectSnapshotAsync(ct);
        var prompt = BuildPrompt(snapshot, userMessage);
        var messageId = BuildMessageId(snapshot.Symbol);
        var fullText = new StringBuilder();

        Logger.LogInformation(
            "[MarketChat] Emit chat: Symbol={Symbol}, Reason={Reason}",
            snapshot.Symbol,
            forceReason ?? "SCHEDULED");

        await StreamSink.StartMessageAsync(messageId, "assistant", name: "market_chat", ct);

        try
        {
            // Use ChatStreamAsync to drive AG-UI streaming (token -> delta)
            var stream = ChatStreamAsync(ChatRequest.Create(prompt), ct);
            await foreach (var delta in stream.WithCancellation(ct))
            {
                if (string.IsNullOrWhiteSpace(delta))
                    continue;
                fullText.Append(delta);
                await StreamSink.AppendDeltaAsync(messageId, delta, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var fallback = BuildFallbackMessage(snapshot.Symbol, snapshot.Tick, ex.Message);
            fullText.Append(fallback);
            await StreamSink.AppendDeltaAsync(messageId, fallback, ct);
        }
        finally
        {
            _chatsEmitted++;
            await StreamSink.EndMessageAsync(messageId, ct);
            await TryTriggerDecisionAsync(snapshot.Symbol, snapshot.Tick, fullText.ToString(), forceReason, ct);
        }
    }

    private (string symbol, MarketTickEvent? tick) PickLatestTick()
    {
        if (_latestTicks.Count == 0)
            return ("UNKNOWN", null);

        MarketTickEvent? latest = null;
        foreach (var kv in _latestTicks)
        {
            if (latest == null)
            {
                latest = kv.Value;
                continue;
            }
            if (kv.Value.Timestamp?.ToDateTime() > latest.Timestamp?.ToDateTime())
                latest = kv.Value;
        }

        return (latest?.Symbol ?? "UNKNOWN", latest);
    }

    private string BuildPrompt(MarketSnapshot snapshot, string? userMessage)
    {
        var symbol = snapshot.Symbol;
        var tick = snapshot.Tick;
        var positions = snapshot.Positions;

        var sb = new StringBuilder();
        sb.AppendLine("请基于以下信息，给出一段简短的市场解读：");
        sb.AppendLine();

        sb.AppendLine($"[Primary] {symbol}");
        if (tick != null)
        {
            sb.AppendLine($"[Price] {tick.Price:F4}");
            sb.AppendLine($"[24h Change] {tick.Change24H:F2}%");
            sb.AppendLine($"[24h Volume] {tick.Volume24H:F0}");
            sb.AppendLine($"[Tick Source] {snapshot.PrimaryTickSource}");
        }
        else
        {
            sb.AppendLine("[Price] N/A (暂无行情)");
            sb.AppendLine($"[Tick Source] {snapshot.PrimaryTickSource}{(string.IsNullOrWhiteSpace(snapshot.PrimaryTickError) ? "" : $" ({snapshot.PrimaryTickError})")}");
        }

        sb.AppendLine();
        sb.AppendLine("[Market Snapshot]");
        foreach (var item in snapshot.SymbolTicks)
        {
            if (item.Tick != null)
            {
                sb.AppendLine(
                    $"{item.Symbol} price={item.Tick.Price:F4} change={item.Tick.Change24H:F2}% vol={item.Tick.Volume24H:F0} source={item.Source}");
            }
            else
            {
                sb.AppendLine(
                    $"{item.Symbol} price=N/A change=N/A vol=N/A source={item.Source}{(string.IsNullOrWhiteSpace(item.Error) ? "" : $" ({item.Error})")}");
            }
        }

        if (_latestSentiment != null)
        {
            sb.AppendLine($"[Sentiment] {_latestSentiment.AnalysisSummary}");
        }
        if (_latestTechnical != null)
        {
            sb.AppendLine($"[Technical] {_latestTechnical.AnalysisSummary}");
        }
        if (_latestNews != null)
        {
            sb.AppendLine($"[News] {_latestNews.AnalysisSummary}");
        }

        if (snapshot.UsdtBalance != null)
        {
            sb.AppendLine($"[Balance] USDT={snapshot.UsdtBalance.Balance:F2}, Available={snapshot.UsdtBalance.Available:F2}");
        }

        sb.AppendLine("[Positions]");
        if (positions.Count == 0)
        {
            sb.AppendLine("None");
        }
        else
        {
            var summary = BuildPositionsSummary(positions);
            foreach (var line in summary)
            {
                sb.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            sb.AppendLine();
            sb.AppendLine("[User Intent]");
            sb.AppendLine(userMessage.Trim());
            sb.AppendLine("请先回应用户关注点，再给出市场判断。");
        }

        sb.AppendLine();
        sb.AppendLine("输出要求：");
        sb.AppendLine("- 用中文输出");
        sb.AppendLine("- 3~6 行以内，短句分行");
        sb.AppendLine("- 给出趋势判断（BULLISH/BEARISH/NEUTRAL），不要给出下单指令");
        sb.AppendLine("- 最后一行必须输出：DECISION_JSON={\"decision\":\"TRIGGER|SKIP\",\"reason\":\"...\",\"confidence\":0-100}");
        sb.AppendLine("- 已提供的数据视为当前快照，不要说“无法获取”；缺失项请标注 N/A");

        return sb.ToString();
    }

    private string BuildFallbackMessage(string symbol, MarketTickEvent? tick, string error)
    {
        var price = tick?.Price is > 0 ? tick.Price.ToString("F4") : "N/A";
        var change = tick != null ? $"{tick.Change24H:F2}%" : "N/A";
        return $"\n[Fallback]\nSymbol={symbol}\nPrice={price}\nChange24H={change}\nLLM error: {error}\n";
    }

    private static string BuildMessageId(string symbol)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var safe = string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.Trim();
        return $"msg:trading:chat:{safe}:{id}";
    }

    private sealed record SymbolTickSnapshot(
        string Symbol,
        MarketTickEvent? Tick,
        string Source,
        string? Error);

    private sealed record MarketSnapshot(
        string Symbol,
        MarketTickEvent? Tick,
        IReadOnlyList<SymbolTickSnapshot> SymbolTicks,
        IReadOnlyList<PositionInfo> Positions,
        BalanceInfo? UsdtBalance,
        string PrimaryTickSource,
        string? PrimaryTickError);

    private async Task<MarketSnapshot> CollectSnapshotAsync(CancellationToken ct)
    {
        var symbols = ResolveSymbols();
        var symbolTicks = new List<SymbolTickSnapshot>();

        foreach (var symbol in symbols)
        {
            var snapshot = await FetchTickSnapshotAsync(symbol, ct);
            symbolTicks.Add(snapshot);
        }

        var primary = PickPrimarySnapshot(symbolTicks);
        var positions = await ResolvePositionsAsync(ct);
        var balance = await ResolveUsdtBalanceAsync(ct);

        return new MarketSnapshot(
            primary.Symbol,
            primary.Tick,
            symbolTicks,
            positions,
            balance,
            primary.Source,
            primary.Error);
    }

    private IReadOnlyList<string> ResolveSymbols()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(DefaultSymbol))
        {
            var s = DefaultSymbol.Trim();
            if (seen.Add(s))
                list.Add(s);
        }

        if (Symbols != null)
        {
            foreach (var raw in Symbols)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var s = raw.Trim();
                if (seen.Add(s))
                    list.Add(s);
            }
        }

        if (list.Count == 0 && _latestTicks.Count > 0)
        {
            foreach (var key in _latestTicks.Keys)
            {
                if (seen.Add(key))
                    list.Add(key);
            }
        }

        return list;
    }

    private SymbolTickSnapshot PickPrimarySnapshot(IReadOnlyList<SymbolTickSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return new SymbolTickSnapshot("UNKNOWN", null, "none", null);

        var preferred = snapshots.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(DefaultSymbol) &&
            s.Symbol.Equals(DefaultSymbol.Trim(), StringComparison.OrdinalIgnoreCase));
        if (preferred != null)
            return preferred;

        var available = snapshots.FirstOrDefault(s => s.Tick != null);
        return available ?? snapshots[0];
    }

    private async Task<SymbolTickSnapshot> FetchTickSnapshotAsync(string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return new SymbolTickSnapshot("UNKNOWN", null, "none", "empty symbol");

        if (_latestTicks.TryGetValue(symbol, out var cached))
            return new SymbolTickSnapshot(symbol, cached, "stream", null);

        if (MarketDataClient == null)
            return new SymbolTickSnapshot(symbol, null, "no-client", "market data client missing");

        try
        {
            var ticker = await MarketDataClient.GetTickerAsync(symbol, ct);
            var tick = ToMarketTickEvent(ticker);
            _latestTicks[symbol] = tick;
            _lastTickUtc = DateTime.UtcNow;
            return new SymbolTickSnapshot(symbol, tick, "rest", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SymbolTickSnapshot(symbol, null, "rest-error", ex.Message);
        }
    }

    private async Task<IReadOnlyList<PositionInfo>> ResolvePositionsAsync(CancellationToken ct)
    {
        if (AccountClient == null)
            return Array.Empty<PositionInfo>();

        try
        {
            return await AccountClient.GetPositionsAsync(null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "[MarketChat] Load positions failed (non-fatal)");
            return Array.Empty<PositionInfo>();
        }
    }

    private async Task<BalanceInfo?> ResolveUsdtBalanceAsync(CancellationToken ct)
    {
        if (AccountClient == null)
            return null;

        try
        {
            var balances = await AccountClient.GetBalancesAsync(ct);
            return balances.FirstOrDefault(b => b.Currency.Equals("USDT", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "[MarketChat] Load balances failed (non-fatal)");
            return null;
        }
    }

    private static MarketTickEvent ToMarketTickEvent(TickerResponse ticker)
    {
        return new MarketTickEvent
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
    }

    private static List<string> BuildPositionsSummary(IReadOnlyList<PositionInfo> positions)
    {
        var lines = new List<string>();
        decimal totalNotional = 0;
        decimal totalUnrealized = 0;

        foreach (var position in positions)
        {
            var sizeAbs = Math.Abs(position.Size);
            var notional = EstimateNotional(position, sizeAbs);
            totalNotional += Math.Abs(notional);
            if (position.UnrealizedPnl.HasValue)
                totalUnrealized += position.UnrealizedPnl.Value;
        }

        lines.Add($"Count={positions.Count}, TotalNotional={totalNotional:F2}, TotalUnrealized={totalUnrealized:F2}");

        lines.Add("Details:");
        const int maxLines = 50;
        var count = 0;
        foreach (var p in positions)
        {
            if (count >= maxLines)
                break;

            var notional = EstimateNotional(p, Math.Abs(p.Size));
            var entry = p.EntryPrice.HasValue ? p.EntryPrice.Value.ToString("F4") : "N/A";
            var mark = p.MarkPrice.HasValue ? p.MarkPrice.Value.ToString("F4") : "N/A";
            var pnl = p.UnrealizedPnl.HasValue ? p.UnrealizedPnl.Value.ToString("F2") : "N/A";
            lines.Add($"- {p.Symbol} {p.Side} size={p.Size:F4} entry={entry} mark={mark} notional={notional:F2} pnl={pnl}");
            count++;
        }

        if (positions.Count > maxLines)
        {
            lines.Add($"... {positions.Count - maxLines} more");
        }

        return lines;
    }

    private static decimal EstimateNotional(PositionInfo position, decimal sizeAbs)
    {
        if (position.Notional.HasValue)
            return position.Notional.Value;
        if (position.MarkPrice.HasValue)
            return position.MarkPrice.Value * sizeAbs;
        if (position.EntryPrice.HasValue)
            return position.EntryPrice.Value * sizeAbs;
        return 0m;
    }

    private async Task EmitUserMessageAsync(string messageId, string content, CancellationToken ct)
    {
        var safe = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId.Trim();
        var msgId = $"msg:trading:chat:user:{safe}";

        await StreamSink!.StartMessageAsync(msgId, "user", name: "user", ct);
        await StreamSink.AppendDeltaAsync(msgId, content, ct);
        await StreamSink.EndMessageAsync(msgId, ct);
    }

    private async Task EmitSystemMessageAsync(string content, CancellationToken ct)
    {
        var msgId = $"msg:trading:chat:system:{Guid.NewGuid().ToString("N")[..10]}";
        await StreamSink!.StartMessageAsync(msgId, "system", name: "market_chat", ct);
        await StreamSink.AppendDeltaAsync(msgId, content, ct);
        await StreamSink.EndMessageAsync(msgId, ct);
    }

    private async Task TryTriggerDecisionAsync(
        string symbol,
        MarketTickEvent? tick,
        string content,
        string? forceReason,
        CancellationToken ct)
    {
        var decision = ParseDecisionSignal(content);
        if (decision == DecisionSignal.Skip)
            return;

        if (tick == null || string.IsNullOrWhiteSpace(symbol) || symbol.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            return;

        var now = DateTime.UtcNow;
        var price = tick.Price;
        var reason = string.IsNullOrWhiteSpace(forceReason) ? "CHAT_SIGNAL" : $"CHAT_SIGNAL:{forceReason}";

        await PublishAsync(new DecisionTriggerEvent
        {
            TriggerId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Reason = reason,
            DeltaPct = 0,
            DeltaAbs = 0,
            BasePrice = price,
            LatestPrice = price,
            Timestamp = Timestamp.FromDateTime(now)
        }, Aevatar.Agents.EventDirection.Up, ct);
    }

    private enum DecisionSignal
    {
        Skip,
        Trigger
    }

    private static DecisionSignal ParseDecisionSignal(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return DecisionSignal.Skip;

        if (TryParseDecisionJson(content, out var decision))
            return decision;

        return ParseDecisionKeyword(content);
    }

    private static DecisionSignal ParseDecisionKeyword(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("DECISION", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.Contains("TRIGGER", StringComparison.OrdinalIgnoreCase))
                return DecisionSignal.Trigger;
            if (line.Contains("SKIP", StringComparison.OrdinalIgnoreCase))
                return DecisionSignal.Skip;
        }

        return DecisionSignal.Skip;
    }

    private static bool TryParseDecisionJson(string content, out DecisionSignal decision)
    {
        decision = DecisionSignal.Skip;
        const string marker = "DECISION_JSON=";
        var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var json = line[(idx + marker.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(json))
                return false;

            return TryParseDecisionJsonPayload(json, out decision);
        }

        return false;
    }

    private static bool TryParseDecisionJsonPayload(string json, out DecisionSignal decision)
    {
        decision = DecisionSignal.Skip;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("decision", out var decisionNode))
                return false;

            var raw = decisionNode.GetString() ?? string.Empty;
            if (raw.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                decision = DecisionSignal.Trigger;
                return true;
            }

            if (raw.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                decision = DecisionSignal.Skip;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}

