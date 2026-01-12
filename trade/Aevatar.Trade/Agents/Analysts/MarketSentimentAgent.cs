using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Aevatar.Trade.Agents.Analysts;

/// <summary>
/// Market sentiment analysis agent
/// Responsibilities: Analyze market sentiment indicators and judge overall market atmosphere
/// </summary>
public class MarketSentimentAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are a senior cryptocurrency market sentiment analyst with extensive knowledge of market psychology and behavioral finance.

        Your task is to judge current market sentiment based on the following market data:

        【Analysis Dimensions】
        1. Fear & Greed Index (0-100)
           - 0-25: Extreme fear → Possible buying opportunity
           - 25-45: Fear
           - 45-55: Neutral
           - 55-75: Greed
           - 75-100: Extreme greed → Possible sell signal

        2. Long/Short Ratio
           - < 0.8: Bears dominate, market bearish
           - 0.8-1.2: Long/short balanced
           - > 1.2: Bulls dominate, market bullish
           - Extreme values may indicate reversal

        3. Funding Rate
           - Positive: Longs pay shorts, bullish sentiment high
           - Negative: Shorts pay longs, bearish sentiment high
           - Extreme positive (>0.1%): Possibly overheated
           - Extreme negative (<-0.05%): Possibly oversold

        4. Open Interest Changes
           - Rising + Price rising: Bulls actively building positions
           - Rising + Price falling: Bears actively building positions
           - Falling: Positions closing, trend may weaken

        【Output Format】
        Please strictly output in the following JSON format:
        {
            "sentiment_score": <integer from -100 to +100>,
            "sentiment_trend": "<UP|DOWN|SIDEWAYS>",
            "signal": "<BULLISH|BEARISH|NEUTRAL>",
            "confidence": <integer from 1-100>,
            "key_observations": ["Observation 1", "Observation 2", "Observation 3"],
            "summary": "<One-sentence summary of current sentiment state>"
        }

        【Notes】
        - Extreme sentiment often indicates reversal
        - Pay attention to divergence between sentiment and price
        - Signals are more reliable when multiple indicators resonate
        - Some indicators may be missing (shown as "N/A"). In that case, do NOT assume default values; base your judgement on available signals only.
        """;

    // ============ State ============

    /// <summary>
    /// Market data client (optional). When set, the agent will fetch contract metrics like
    /// funding rate / open interest instead of using placeholders.
    /// </summary>
    public IWeexApiClient? ApiClient { get; set; }

    private readonly SentimentAnalystState _sentimentState = new();
    private int _tickCounter;
    private DateTime _lastAnalysisUtc = DateTime.MinValue;
    private int _analysisRunning;
    private const int MinAnalysisIntervalSeconds = 1;
    // NOTE: Must be < LocalMessageStream capacity (default 1000) * tick interval (1s),
    // otherwise the DataCollector can be back-pressured and appear "stuck".
    // 300s is generous and matches DecisionEngine default timeout.
    private const int LlmTimeoutSeconds = 300;

    // Market indicator cache (avoid hammering WEEX endpoints every second)
    private readonly Dictionary<string, MarketIndicatorSnapshot> _indicatorCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int IndicatorCacheTtlSeconds = 15;

    private sealed class MarketIndicatorSnapshot
    {
        public DateTime FetchedAtUtc { get; init; }
        public double? FundingRateRatio { get; init; }   // 0.0001 means 0.01%
        public double? OpenInterest { get; init; }
    }
    
    // 交易所 REST 指标请求必须有超时，否则会把 Agent stream 永久卡死（LocalMessageStream 会反压，最终让系统“看起来停了”）。
    private const int IndicatorFetchTimeoutSeconds = 10;

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _sentimentState.AgentId = Id.ToString();
        Logger.LogInformation("[SentimentAgent] Activated: {AgentId}", _sentimentState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"MarketSentimentAgent: Score={_sentimentState.CurrentSentiment}, " +
            $"Analyses={_sentimentState.AnalysisCount}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// Handle market data and periodically trigger sentiment analysis
    /// </summary>
    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        // ------------------------------------------------------------
        //  Analyze every N ticks to avoid being too frequent.
        //
        //  NOTE:
        //  - Do NOT gate by AnalysisCount (it only increments when analysis runs),
        //    otherwise it will analyze once then never again.
        // ------------------------------------------------------------
        _tickCounter++;

        // First analysis ASAP for cold start.
        // Then high frequency (user要求：LLM调用无上限)，但保证同一时间只跑一个请求，避免堆积。
        var now = DateTime.UtcNow;
        if (_sentimentState.AnalysisCount > 0 && (now - _lastAnalysisUtc).TotalSeconds < MinAnalysisIntervalSeconds)
            return;

        if (Interlocked.Exchange(ref _analysisRunning, 1) == 1)
            return;

        _lastAnalysisUtc = now;
        try
        {
            await AnalyzeSentimentAsync(evt.Symbol, evt);
        }
        finally
        {
            Interlocked.Exchange(ref _analysisRunning, 0);
        }
    }

    // ============ Analysis Methods ============

    /// <summary>
    /// Execute sentiment analysis
    /// </summary>
    public async Task AnalyzeSentimentAsync(
        string symbol,
        MarketTickEvent? latestTick = null,
        double? fearGreedIndex = null,
        double? longShortRatio = null,
        double? fundingRate = null,
        double? openInterest = null)
    {
        // Best-effort fetch (only when caller didn't provide these fields).
        // NOTE: fear/greed + long/short ratio are not available in WEEX AI Wars market APIs (as far as current skills cover),
        // so we keep them as null (display as N/A) instead of faking 50 / 1.0.
        if (fundingRate == null || openInterest == null)
        {
            var (fund, oi) = await GetMarketIndicatorsBestEffortAsync(symbol);
            fundingRate ??= fund;
            openInterest ??= oi;
        }

        var prompt = BuildAnalysisPrompt(
            symbol, latestTick, fearGreedIndex, longShortRatio, fundingRate, openInterest);

        try
        {
            // IMPORTANT:
            // - This runs inside the agent's event handler chain (stream is serial).
            // - If LLM hangs forever, this stream blocks, its queue fills, and DataCollector publish will back-pressure.
            // - Guard with WaitAsync so we ALWAYS release the stream.
            var timeout = TimeSpan.FromSeconds(LlmTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);

            ChatResponse chat;
            try
            {
                var task = ChatAsync(ChatRequest.Create(prompt), timeoutCts.Token);
                chat = await task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                timeoutCts.Cancel();
                Logger.LogWarning(
                    "[SentimentAgent] LLM timeout >{Timeout}s for {Symbol}. Skip this analysis cycle.",
                    LlmTimeoutSeconds, symbol);
                return;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Logger.LogWarning(
                    "[SentimentAgent] LLM timeout >{Timeout}s for {Symbol}. Skip this analysis cycle.",
                    LlmTimeoutSeconds, symbol);
                return;
            }

            var analysis = ParseAnalysisResponse(
                chat.Content ?? string.Empty,
                symbol,
                fearGreedIndex,
                longShortRatio,
                fundingRate,
                openInterest);

            // Update state
            _sentimentState.CurrentSentiment = analysis.SentimentScore;
            _sentimentState.AnalysisCount++;
            _sentimentState.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // Keep historical records
            if (_sentimentState.SentimentHistory.Count >= 100)
                _sentimentState.SentimentHistory.RemoveAt(0);
            _sentimentState.SentimentHistory.Add(analysis.SentimentScore);

            // Publish analysis results
            // IMPORTANT (hierarchy):
            // - SentimentAgent is a child of Coordinator.
            // - Publish Up so the Coordinator can receive the analysis.
            await PublishAsync(analysis, Aevatar.Agents.EventDirection.Up);

            Logger.LogInformation(
                "[SentimentAgent] Analysis completed for {Symbol}: Score={Score}, Signal={Signal}",
                symbol, analysis.SentimentScore, analysis.AnalysisSummary);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SentimentAgent] Analysis failed for {Symbol}", symbol);
        }
    }

    // ============ Private Methods ============

    private string BuildAnalysisPrompt(
        string symbol,
        MarketTickEvent? tick,
        double? fearGreedIndex,
        double? longShortRatio,
        double? fundingRate,
        double? openInterest)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Please analyze the market sentiment for {symbol}:");
        sb.AppendLine();

        if (tick != null)
        {
            sb.AppendLine("【Current Market Data】");
            sb.AppendLine($"- Price: {tick.Price:F2}");
            sb.AppendLine($"- 24h Change: {tick.Change24H:F2}%");
            sb.AppendLine($"- 24h Volume: {tick.Volume24H:F0}");
            sb.AppendLine();
        }

        sb.AppendLine("【Sentiment Indicators】");
        sb.AppendLine($"- Fear & Greed Index: {(fearGreedIndex.HasValue ? fearGreedIndex.Value.ToString("F0") : "N/A")}");
        sb.AppendLine($"- Long/Short Ratio: {(longShortRatio.HasValue ? longShortRatio.Value.ToString("F2") : "N/A")}");
        sb.AppendLine($"- Funding Rate: {(fundingRate.HasValue ? $"{fundingRate.Value * 100:F4}%" : "N/A")}");
        sb.AppendLine($"- Open Interest: {(openInterest.HasValue ? openInterest.Value.ToString("F0") : "N/A")}");
        sb.AppendLine();
        sb.AppendLine("NOTE: If an indicator is N/A, ignore it and do not invent numbers.");

        return sb.ToString();
    }

    private MarketSentimentAnalysisEvent ParseAnalysisResponse(
        string response,
        string symbol,
        double? fearGreedIndex,
        double? longShortRatio,
        double? fundingRate,
        double? openInterest)
    {
        // Try to parse JSON response
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = root.TryGetProperty("sentiment_score", out var score) 
                    ? score.GetInt32() : 0,
                SentimentTrend = root.TryGetProperty("sentiment_trend", out var trend) 
                    ? trend.GetString() ?? "SIDEWAYS" : "SIDEWAYS",
                FearGreedIndex = fearGreedIndex ?? double.NaN,
                LongShortRatio = longShortRatio ?? double.NaN,
                FundingRate = fundingRate ?? double.NaN,
                OpenInterest = openInterest ?? double.NaN,
                Confidence = root.TryGetProperty("confidence", out var conf) 
                    ? conf.GetInt32() : 50,
                AnalysisSummary = root.TryGetProperty("summary", out var summary) 
                    ? summary.GetString() ?? "" : response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            // If parsing fails, return default values
            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = 0,
                SentimentTrend = "SIDEWAYS",
                FearGreedIndex = fearGreedIndex ?? double.NaN,
                LongShortRatio = longShortRatio ?? double.NaN,
                FundingRate = fundingRate ?? double.NaN,
                OpenInterest = openInterest ?? double.NaN,
                Confidence = 30,
                AnalysisSummary = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    private async Task<(double? fundingRateRatio, double? openInterest)> GetMarketIndicatorsBestEffortAsync(string symbol)
    {
        try
        {
            if (ApiClient == null)
                return (null, null);

            if (_indicatorCache.TryGetValue(symbol, out var cached))
            {
                var age = DateTime.UtcNow - cached.FetchedAtUtc;
                if (age.TotalSeconds < IndicatorCacheTtlSeconds)
                    return (cached.FundingRateRatio, cached.OpenInterest);
            }

            var timeout = TimeSpan.FromSeconds(IndicatorFetchTimeoutSeconds);
            using var timeoutCts = new CancellationTokenSource(timeout);

            var fundTask = ApiClient.GetCurrentFundingRateAsync(symbol, timeoutCts.Token);
            var oiTask = ApiClient.GetOpenInterestAsync(symbol, timeoutCts.Token);
            try
            {
                await Task.WhenAll(fundTask, oiTask).WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                timeoutCts.Cancel();
                // Backoff via cache so we don't hammer WEEX when it is slow/unreachable.
                _indicatorCache[symbol] = new MarketIndicatorSnapshot
                {
                    FetchedAtUtc = DateTime.UtcNow,
                    FundingRateRatio = null,
                    OpenInterest = null
                };
                Logger.LogDebug(
                    "[SentimentAgent] Market indicator fetch timeout >{Timeout}s for {Symbol}",
                    IndicatorFetchTimeoutSeconds, symbol);
                return (null, null);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _indicatorCache[symbol] = new MarketIndicatorSnapshot
                {
                    FetchedAtUtc = DateTime.UtcNow,
                    FundingRateRatio = null,
                    OpenInterest = null
                };
                Logger.LogDebug(
                    "[SentimentAgent] Market indicator fetch timeout >{Timeout}s for {Symbol}",
                    IndicatorFetchTimeoutSeconds, symbol);
                return (null, null);
            }

            var funding = (await fundTask) is { } f ? (double?) (double)f : null;
            var oi = (await oiTask) is { } o ? (double?) (double)o : null;

            _indicatorCache[symbol] = new MarketIndicatorSnapshot
            {
                FetchedAtUtc = DateTime.UtcNow,
                FundingRateRatio = funding,
                OpenInterest = oi
            };

            return (funding, oi);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[SentimentAgent] Failed to fetch market indicators (funding/openInterest) for {Symbol}", symbol);
            return (null, null);
        }
    }
}
