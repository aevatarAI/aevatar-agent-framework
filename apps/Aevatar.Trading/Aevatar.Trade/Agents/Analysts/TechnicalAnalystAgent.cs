using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Aevatar.Trade.Agents.Analysts;

/// <summary>
/// Technical analysis agent
/// Responsibilities: Perform technical analysis based on price data, identify trends and trading signals
/// </summary>
public class TechnicalAnalystAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are a professional cryptocurrency technical analyst, proficient in various technical indicators and chart pattern analysis.

        【Technical Indicator Interpretation】

        1. RSI (Relative Strength Index, 14-period)
           - < 30: Oversold zone, possible bounce
           - 30-70: Normal range
           - > 70: Overbought zone, possible pullback
           - RSI divergence is an important signal

        2. MACD
           - MACD > Signal: Bullish momentum
           - MACD < Signal: Bearish momentum
           - Golden cross: MACD crosses above Signal, buy signal
           - Death cross: MACD crosses below Signal, sell signal
           - Histogram expanding: Trend strengthening
           - Histogram contracting: Trend weakening

        3. Moving Average System
           - Price > MA20 > MA60: Bullish alignment
           - Price < MA20 < MA60: Bearish alignment
           - MA crossover: Trend change signal

        4. Bollinger Bands
           - Touching upper band: Possibly overbought
           - Touching lower band: Possibly oversold
           - Band narrowing: Breakout imminent
           - Band expanding: Volatility increasing

        5. ATR (Average True Range)
           - Used for setting stop loss distance
           - ATR expanding: Volatility increasing
           - ATR contracting: Volatility decreasing

        【Output Format】
        Please strictly output in the following JSON format:
        {
            "trend_direction": "<BULLISH|BEARISH|SIDEWAYS>",
            "trend_strength": <integer from 1-10>,
            "signal": "<BUY|SELL|HOLD>",
            "support_level": <support price>,
            "resistance_level": <resistance price>,
            "pattern_detected": "<detected pattern, empty if none>",
            "confidence": <integer from 1-100>,
            "key_observations": ["Observation 1", "Observation 2", "Observation 3"],
            "summary": "<One-sentence summary of technical condition>"
        }

        【Notes】
        - Signals are more reliable when multiple indicators resonate
        - Pay attention to divergence between indicators and price
        - Longer timeframe trends take priority over shorter timeframe signals
        - Confirm breakouts at key support/resistance levels
        """;

    // ============ State ============

    private readonly TechnicalAnalystState _techState = new();
    private readonly List<KlineUpdateEvent> _klineBuffer = new();
    private DateTime _lastAnalysisUtc = DateTime.MinValue;
    private int _analysisRunning;
    private const int KlineBufferSize = 200;
    private const int MinAnalysisIntervalSeconds = 1;

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _techState.AgentId = Id.ToString();
        Logger.LogInformation("[TechnicalAgent] Activated: {AgentId}", _techState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TechnicalAnalystAgent: Trend={_techState.CurrentTrend}, " +
            $"Strength={_techState.TrendStrength}, " +
            $"Analyses={_techState.AnalysisCount}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// Handle kline data, update buffer and trigger analysis
    /// </summary>
    [EventHandler]
    public async Task HandleKlineUpdate(KlineUpdateEvent evt)
    {
        // Update kline buffer
        _klineBuffer.Add(evt);
        if (_klineBuffer.Count > KlineBufferSize)
            _klineBuffer.RemoveAt(0);

        // 高频模式（用户要求）：只要有 K 线输入，就尽可能频繁地产出技术分析，
        // 但用“1秒节流 + 互斥”避免并发堆积。
        await TryAnalyzeAsync(string.IsNullOrWhiteSpace(evt.Symbol) ? "UNKNOWN" : evt.Symbol);
    }

    /// <summary>
    /// High-frequency trigger: MarketTick also drives technical analysis.
    /// This keeps Coordinator's technical snapshot from becoming stale when kline interval is large (e.g. 5m/15m).
    /// </summary>
    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Symbol))
            return;

        await TryAnalyzeAsync(evt.Symbol);
    }

    // ============ Analysis Methods ============

    /// <summary>
    /// Execute technical analysis
    /// </summary>
    public async Task AnalyzeTechnicalAsync(string symbol)
    {
        if (_klineBuffer.Count < 60)
        {
            Logger.LogDebug("[TechnicalAgent] Insufficient data for analysis");
            return;
        }

        // Calculate technical indicators
        var indicators = CalculateIndicators();
        var prompt = BuildAnalysisPrompt(symbol, indicators);

        try
        {
            var chat = await ChatAsync(ChatRequest.Create(prompt));
            var analysis = ParseAnalysisResponse(chat.Content ?? string.Empty, symbol, indicators);

            // Update state
            _techState.CurrentTrend = analysis.TrendDirection;
            _techState.TrendStrength = analysis.TrendStrength;
            _techState.LastRsi = analysis.Rsi;
            _techState.LastMacd = analysis.Macd;
            _techState.AnalysisCount++;
            _techState.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // Publish analysis results
            // IMPORTANT:
            // - TechnicalAgent and Coordinator are siblings under DataCollector.
            // - Publish Up so DataCollector can fan-out the analysis to all siblings (including Coordinator).
            await PublishAsync(analysis, Aevatar.Agents.EventDirection.Up);

            Logger.LogInformation(
                "[TechnicalAgent] Analysis completed for {Symbol}: Trend={Trend}, Signal={Signal}",
                symbol, analysis.TrendDirection, analysis.Signal);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[TechnicalAgent] Analysis failed for {Symbol}", symbol);
        }
    }

    private async Task TryAnalyzeAsync(string symbol)
    {
        if (_klineBuffer.Count < 60)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastAnalysisUtc).TotalSeconds < MinAnalysisIntervalSeconds)
            return;

        if (Interlocked.Exchange(ref _analysisRunning, 1) == 1)
            return;

        _lastAnalysisUtc = now;
        try
        {
            await AnalyzeTechnicalAsync(symbol);
        }
        finally
        {
            Interlocked.Exchange(ref _analysisRunning, 0);
        }
    }

    // ============ Technical Indicators ============

    private TechnicalIndicators CalculateIndicators()
    {
        var closes = _klineBuffer.Select(k => k.Close).ToList();
        var highs = _klineBuffer.Select(k => k.High).ToList();
        var lows = _klineBuffer.Select(k => k.Low).ToList();

        return new TechnicalIndicators
        {
            CurrentPrice = closes.Last(),
            RSI = CalculateRSI(closes, 14),
            MACD = CalculateMACD(closes),
            MA20 = CalculateSMA(closes, 20),
            MA60 = CalculateSMA(closes, 60),
            BollingerUpper = CalculateBollingerUpper(closes, 20, 2),
            BollingerLower = CalculateBollingerLower(closes, 20, 2),
            ATR = CalculateATR(highs, lows, closes, 14),
            Support = FindSupport(lows),
            Resistance = FindResistance(highs)
        };
    }

    private double CalculateRSI(List<double> closes, int period)
    {
        if (closes.Count < period + 1) return 50;

        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = closes.Count - period; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(Math.Max(0, change));
            losses.Add(Math.Max(0, -change));
        }

        var avgGain = gains.Average();
        var avgLoss = losses.Average();

        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private (double MACD, double Signal, double Histogram) CalculateMACD(List<double> closes)
    {
        var ema12 = CalculateEMA(closes, 12);
        var ema26 = CalculateEMA(closes, 26);
        var macd = ema12 - ema26;

        // Simplified: Signal uses 9-period EMA of MACD
        var signal = macd * 0.9; // Simplified calculation
        var histogram = macd - signal;

        return (macd, signal, histogram);
    }

    private double CalculateSMA(List<double> values, int period)
    {
        if (values.Count < period) return values.Average();
        return values.Skip(values.Count - period).Average();
    }

    private double CalculateEMA(List<double> values, int period)
    {
        if (values.Count < period) return values.Average();

        var multiplier = 2.0 / (period + 1);
        var ema = values.Take(period).Average();

        foreach (var value in values.Skip(period))
        {
            ema = (value - ema) * multiplier + ema;
        }

        return ema;
    }

    private double CalculateBollingerUpper(List<double> closes, int period, double stdDevMult)
    {
        var sma = CalculateSMA(closes, period);
        var stdDev = CalculateStdDev(closes.TakeLast(period).ToList());
        return sma + stdDevMult * stdDev;
    }

    private double CalculateBollingerLower(List<double> closes, int period, double stdDevMult)
    {
        var sma = CalculateSMA(closes, period);
        var stdDev = CalculateStdDev(closes.TakeLast(period).ToList());
        return sma - stdDevMult * stdDev;
    }

    private double CalculateStdDev(List<double> values)
    {
        var avg = values.Average();
        var sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquares / values.Count);
    }

    private double CalculateATR(List<double> highs, List<double> lows, List<double> closes, int period)
    {
        if (highs.Count < period + 1) return 0;

        var trueRanges = new List<double>();
        for (int i = highs.Count - period; i < highs.Count; i++)
        {
            var tr = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])));
            trueRanges.Add(tr);
        }

        return trueRanges.Average();
    }

    private double FindSupport(List<double> lows)
    {
        var recentLows = lows.TakeLast(20).ToList();
        return recentLows.Min();
    }

    private double FindResistance(List<double> highs)
    {
        var recentHighs = highs.TakeLast(20).ToList();
        return recentHighs.Max();
    }

    // ============ Helper Methods ============

    private string BuildAnalysisPrompt(string symbol, TechnicalIndicators ind)
    {
        return $"""
            Please analyze the technical aspects of {symbol}:

            【Current Price】
            - Price: {ind.CurrentPrice:F2}

            【Technical Indicators】
            - RSI(14): {ind.RSI:F2}
            - MACD: {ind.MACD.MACD:F4}
            - MACD Signal: {ind.MACD.Signal:F4}
            - MACD Histogram: {ind.MACD.Histogram:F4}
            - MA20: {ind.MA20:F2}
            - MA60: {ind.MA60:F2}
            - Bollinger Upper: {ind.BollingerUpper:F2}
            - Bollinger Lower: {ind.BollingerLower:F2}
            - ATR(14): {ind.ATR:F2}

            【Key Levels】
            - Support: {ind.Support:F2}
            - Resistance: {ind.Resistance:F2}

            【Moving Average Position】
            - Price vs MA20: {(ind.CurrentPrice > ind.MA20 ? "Above" : "Below")}
            - Price vs MA60: {(ind.CurrentPrice > ind.MA60 ? "Above" : "Below")}
            - MA20 vs MA60: {(ind.MA20 > ind.MA60 ? "Bullish alignment" : "Bearish alignment")}
            """;
    }

    private TechnicalAnalysisEvent ParseAnalysisResponse(
        string response, 
        string symbol, 
        TechnicalIndicators ind)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            return new TechnicalAnalysisEvent
            {
                Symbol = symbol,
                TrendDirection = root.TryGetProperty("trend_direction", out var trend)
                    ? trend.GetString() ?? "SIDEWAYS" : "SIDEWAYS",
                TrendStrength = root.TryGetProperty("trend_strength", out var strength)
                    ? strength.GetInt32() : 5,
                Signal = root.TryGetProperty("signal", out var signal)
                    ? signal.GetString() ?? "HOLD" : "HOLD",
                SupportLevel = ind.Support,
                ResistanceLevel = ind.Resistance,
                Rsi = ind.RSI,
                Macd = ind.MACD.MACD,
                MacdSignal = ind.MACD.Signal,
                MacdHistogram = ind.MACD.Histogram,
                Ma20 = ind.MA20,
                Ma60 = ind.MA60,
                BollingerUpper = ind.BollingerUpper,
                BollingerLower = ind.BollingerLower,
                Atr = ind.ATR,
                PatternDetected = root.TryGetProperty("pattern_detected", out var pattern)
                    ? pattern.GetString() ?? "" : "",
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetInt32() : 50,
                AnalysisSummary = root.TryGetProperty("summary", out var summary)
                    ? summary.GetString() ?? "" : response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            return new TechnicalAnalysisEvent
            {
                Symbol = symbol,
                TrendDirection = "SIDEWAYS",
                TrendStrength = 5,
                Signal = "HOLD",
                Rsi = ind.RSI,
                Macd = ind.MACD.MACD,
                Confidence = 30,
                AnalysisSummary = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    // ============ Helper Types ============

    private record TechnicalIndicators
    {
        public double CurrentPrice { get; init; }
        public double RSI { get; init; }
        public (double MACD, double Signal, double Histogram) MACD { get; init; }
        public double MA20 { get; init; }
        public double MA60 { get; init; }
        public double BollingerUpper { get; init; }
        public double BollingerLower { get; init; }
        public double ATR { get; init; }
        public double Support { get; init; }
        public double Resistance { get; init; }
    }
}
