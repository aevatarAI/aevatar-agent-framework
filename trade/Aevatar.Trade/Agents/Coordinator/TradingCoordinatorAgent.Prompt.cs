using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Trade.Agents.Coordinator;

public partial class TradingCoordinatorAgent
{
    private string BuildDecisionPrompt(string symbol)
    {
        static string Clip(string? s, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";
            var x = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (x.Length <= maxLen)
                return x;
            return x[..maxLen] + "…";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Make ONE trading decision for the symbol using the latest snapshot below.");
        sb.AppendLine("Mode: Aggressive Micro-Scalping (tiny probe trades, quick exits).");
        sb.AppendLine("Missing reports = NEUTRAL. HOLD should be rare.");
        sb.AppendLine();

        // Current price snapshot (for fast reaction)
        if (!string.IsNullOrWhiteSpace(symbol) &&
            _latestTickBySymbol.TryGetValue(symbol, out var tick) &&
            tick != null &&
            tick.Price > 0)
        {
            sb.AppendLine("【Market Tick Snapshot】");
            sb.AppendLine($"- Symbol: {tick.Symbol}");
            sb.AppendLine($"- Price: {tick.Price:F2}");
            sb.AppendLine($"- Bid/Ask: {tick.Bid:F2} / {tick.Ask:F2}");
            sb.AppendLine($"- 24h Change: {tick.Change24H:F2}%");
            sb.AppendLine($"- Timestamp(UTC): {tick.Timestamp.ToDateTime():O}");
            sb.AppendLine();
        }

        // Sentiment analysis (per symbol)
        if (!string.IsNullOrWhiteSpace(symbol) &&
            _latestSentimentBySymbol.TryGetValue(symbol, out var s) &&
            s != null)
        {
            var fearGreed = double.IsNaN(s.FearGreedIndex) ? "N/A" : s.FearGreedIndex.ToString("F0");
            var longShort = double.IsNaN(s.LongShortRatio) ? "N/A" : s.LongShortRatio.ToString("F2");
            var funding = double.IsNaN(s.FundingRate) ? "N/A" : $"{s.FundingRate * 100:F4}%";
            var openInterest = double.IsNaN(s.OpenInterest) ? "N/A" : s.OpenInterest.ToString("F0");
            sb.AppendLine("【Market Sentiment Analyst Report】");
            sb.AppendLine($"- Sentiment Score: {s.SentimentScore} (-100~+100)");
            sb.AppendLine($"- Sentiment Trend: {s.SentimentTrend}");
            sb.AppendLine($"- Fear & Greed Index: {fearGreed}");
            sb.AppendLine($"- Long/Short Ratio: {longShort}");
            sb.AppendLine($"- Funding Rate: {funding}");
            sb.AppendLine($"- Open Interest: {openInterest}");
            sb.AppendLine($"- Summary: {Clip(s.AnalysisSummary, 160)}");
            sb.AppendLine($"- Confidence: {s.Confidence}%");
            sb.AppendLine();
        }

        // Technical analysis (per symbol)
        if (!string.IsNullOrWhiteSpace(symbol) &&
            _latestTechnicalBySymbol.TryGetValue(symbol, out var t) &&
            t != null)
        {
            sb.AppendLine("【Technical Analyst Report】");
            sb.AppendLine($"- Trend Direction: {t.TrendDirection}");
            sb.AppendLine($"- Trend Strength: {t.TrendStrength}/10");
            sb.AppendLine($"- Trading Signal: {t.Signal}");
            sb.AppendLine($"- RSI: {t.Rsi:F2}");
            sb.AppendLine($"- MACD: {t.Macd:F4}");
            sb.AppendLine($"- Support Level: {t.SupportLevel:F2}");
            sb.AppendLine($"- Resistance Level: {t.ResistanceLevel:F2}");
            if (!string.IsNullOrEmpty(t.PatternDetected))
                sb.AppendLine($"- Pattern Detected: {t.PatternDetected}");
            sb.AppendLine($"- Summary: {Clip(t.AnalysisSummary, 160)}");
            sb.AppendLine($"- Confidence: {t.Confidence}%");
            sb.AppendLine();
        }

        // News analysis (per symbol)
        if (!string.IsNullOrWhiteSpace(symbol) &&
            _latestNewsBySymbol.TryGetValue(symbol, out var n) &&
            n != null)
        {
            sb.AppendLine("【News Analyst Report】");
            sb.AppendLine($"- Headline: {n.Headline}");
            sb.AppendLine($"- Impact Type: {n.ImpactType}");
            sb.AppendLine($"- Impact Level: {n.ImpactLevel}");
            sb.AppendLine($"- Impact Duration: {n.ImpactDuration}");
            sb.AppendLine($"- Summary: {Clip(n.AnalysisSummary, 160)}");
            sb.AppendLine($"- Confidence: {n.Confidence}%");
            sb.AppendLine();
        }

        sb.AppendLine("Constraints:");
        sb.AppendLine($"- min_confidence_to_trade: {_minConfidenceToTrade}%");
        sb.AppendLine("- If direction is BUY/SELL: confidence MUST be >= 50.");
        sb.AppendLine("- position_pct: prefer 0.5~2.0 for micro-scalp (0 for HOLD).");
        sb.AppendLine();
        sb.AppendLine("Output rules (STRICT):");
        sb.AppendLine("- Return ONLY one JSON object. No markdown. No code fences. No extra text.");
        sb.AppendLine("- reasoning fields MUST be Chinese, concise, and directly based on provided data.");
        sb.AppendLine("- reasoning.final_logic must be short (<= 8 lines).");
        sb.AppendLine("""
{
  "direction": "BUY|SELL|HOLD",
  "confidence": 1-100,
  "position_pct": 0-30,
  "reasoning": {
    "sentiment_factor": "...",
    "technical_factor": "...",
    "news_factor": "...",
    "final_logic": "..."
  },
  "summary": "..."
}
""");

        return sb.ToString();
    }

    private TradingDecisionEvent ParseDecisionResponse(string response, string symbol)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            var reasoning = "";
            if (root.TryGetProperty("reasoning", out var reasoningObj))
            {
                var parts = new List<string>();
                if (reasoningObj.TryGetProperty("sentiment_factor", out var sf))
                    parts.Add($"Sentiment: {sf.GetString()}");
                if (reasoningObj.TryGetProperty("technical_factor", out var tf))
                    parts.Add($"Technical: {tf.GetString()}");
                if (reasoningObj.TryGetProperty("news_factor", out var nf))
                    parts.Add($"News: {nf.GetString()}");
                if (reasoningObj.TryGetProperty("final_logic", out var fl))
                    parts.Add($"Decision Logic: {fl.GetString()}");
                reasoning = string.Join(" | ", parts);
            }

            _latestSentimentBySymbol.TryGetValue(symbol, out var s);
            _latestTechnicalBySymbol.TryGetValue(symbol, out var t);
            _latestNewsBySymbol.TryGetValue(symbol, out var n);

            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = root.TryGetProperty("direction", out var dir)
                    ? dir.GetString() ?? "HOLD" : "HOLD",
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetInt32() : 50,
                SuggestedPositionPct = root.TryGetProperty("position_pct", out var pos)
                    ? pos.GetDouble() : 10,
                SentimentSummary = s?.AnalysisSummary ?? "",
                TechnicalSummary = t?.AnalysisSummary ?? "",
                NewsSummary = n?.AnalysisSummary ?? "",
                Reasoning = reasoning,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 30,
                Reasoning = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    private static string TrimReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "";
        var s = reason.Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length > 180)
            s = s[..180] + "…";
        return s;
    }
}


