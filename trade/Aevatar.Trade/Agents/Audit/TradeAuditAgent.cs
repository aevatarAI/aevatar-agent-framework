using System.Text;
using System.Text.Json;
using System.Linq;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Audit;

/// <summary>
/// Trade audit agent (JSONL sink).
///
/// Goals:
/// - Capture key events for replay/debug/demo.
/// - Produce artifacts that can later be uploaded to WEEX AI Wars "Upload AI log".
///
/// NOTE:
/// - This agent intentionally writes *append-only* JSONL for simplicity and robustness.
/// - High-frequency market data logging is disabled by default to avoid huge files.
/// </summary>
public sealed class TradeAuditAgent : GAgentBase<TradeAuditState>
{
    // ---------------------------------------------------------------------
    //  Encoding policy (IMPORTANT)
    //
    //  Why:
    //  - Encoding.UTF8 in .NET emits a UTF-8 BOM (EF BB BF) when creating a new file.
    //  - JSONL expects each line to be valid JSON; a leading BOM breaks strict parsers.
    //
    //  Rule:
    //  - Always write UTF-8 *without* BOM.
    // ---------------------------------------------------------------------
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly TypeRegistry TradeTypeRegistry = TypeRegistry.FromMessages(
        // Core trade events
        MarketTickEvent.Descriptor,
        KlineUpdateEvent.Descriptor,
        MarketSentimentAnalysisEvent.Descriptor,
        TechnicalAnalysisEvent.Descriptor,
        NewsImpactAnalysisEvent.Descriptor,
        TradingDecisionEvent.Descriptor,
        DecisionCycleStartedEvent.Descriptor,
        DecisionCycleCompletedEvent.Descriptor,
        ApprovedTradeEvent.Descriptor,
        TradeRejectedEvent.Descriptor,
        OrderExecutedEvent.Descriptor,
        OrderSimulatedEvent.Descriptor,
        OrderFailedEvent.Descriptor,
        OrderCancelledEvent.Descriptor,
        CircuitBreakerTriggeredEvent.Descriptor,
        AiWarsLogUploadRequestedEvent.Descriptor,
        AiWarsLogUploadSucceededEvent.Descriptor,
        AiWarsLogUploadFailedEvent.Descriptor
    );

    private static readonly JsonFormatter EnvelopeJsonFormatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: TradeTypeRegistry));

    private bool _includeMarketData;
    private bool _requestAiWarsUpload;
    private string _aiModel = "unknown";

    // 只在运行期用：避免同一 decision 重复触发上传（不写入 State，重启后自然重置）
    private readonly HashSet<string> _aiWarsUploadRequestedDecisionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cycleIdByDecisionId = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------------
    //  AI Wars Upload batching（多币对合并上传）
    //
    //  现象：多 symbol 场景下，每个 symbol 都上传一次 uploadAiLog → 噪声大、回执多、成本高。
    //  本质：uploadAiLog 的 input/output/explanation 支持 object，我们可以把“本轮多币对决策”一次上传。
    //  方案：对 BUY/SELL 决策做 debounce（默认 2s），聚合为一个 batch payload，再触发一次上传。
    // ---------------------------------------------------------------------
    private readonly object _aiWarsBatchLock = new();
    private readonly List<(string CycleId, string DecisionId, DateTime AtUtc)> _aiWarsPendingDecisionBatch = new();
    private int _aiWarsBatchFlushScheduled;
    private const int AiWarsBatchDebounceMs = 2000;

    // ---------------------------------------------------------------------
    //  人类可读策略日志（Markdown）
    //  - 目标：让比赛 Demo “一眼看懂 AI 在想什么、为什么下单、结果如何”
    //  - 策略：按 DecisionCycleCompletedEvent 触发一次汇总落盘（append-only）
    // ---------------------------------------------------------------------
    private const int DefaultMaxMarkdownBytes = 1_000_000; // 1MB per segment (human-friendly)
    private int _maxMarkdownBytes = DefaultMaxMarkdownBytes;
    private int _nextMarkdownPartIndex = 1; // Next archive part number (part0001, part0002, ...)

    // Active markdown file is always stable: trade_audit_<runId>.md
    // When it grows too large, we archive it to trade_audit_<runId>_partNNNN.md and start a fresh active file.
    private string _markdownFile = "";

    private readonly Dictionary<string, DecisionCycleStartedEvent> _cycleStarted = new();
    private readonly Dictionary<string, DecisionCycleCompletedEvent> _cycleCompleted = new();

    // NOTE: 这些事件本身已是 Protobuf；缓存仅用于本轮运行的“汇总输出”，不写入 State。
    private readonly Dictionary<string, TradingDecisionEvent> _decisionsById = new();
    private readonly Dictionary<string, ApprovedTradeEvent> _approvedByDecisionId = new();
    private readonly Dictionary<string, TradeRejectedEvent> _rejectedByDecisionId = new();
    private readonly Dictionary<string, OrderExecutedEvent> _executedByDecisionId = new();
    private readonly Dictionary<string, OrderSimulatedEvent> _simulatedByDecisionId = new();
    private readonly Dictionary<string, OrderFailedEvent> _failedByDecisionId = new();

    // Latest analysis snapshot (per symbol) to enrich human log.
    private readonly Dictionary<string, MarketSentimentAnalysisEvent> _latestSentiment = new();
    private readonly Dictionary<string, TechnicalAnalysisEvent> _latestTechnical = new();
    private readonly Dictionary<string, NewsImpactAnalysisEvent> _latestNews = new();

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Stable unified id (AgentType:RawId)
        State.AgentId = Id.ToString();

        if (string.IsNullOrWhiteSpace(State.AuditRunId))
        {
            State.AuditRunId = Guid.NewGuid().ToString("N")[..12];
        }

        if (string.IsNullOrWhiteSpace(State.OutputDir))
        {
            // Default to a local folder under current working directory
            State.OutputDir = "trade-audit";
        }

        if (string.IsNullOrWhiteSpace(State.CurrentFile))
        {
            State.CurrentFile = $"trade_audit_{State.AuditRunId}.jsonl";
        }

        Directory.CreateDirectory(State.OutputDir);

        InitializeMarkdownRotationState();

        Logger.LogInformation(
            "[TradeAudit] Activated: AgentId={AgentId}, Run={RunId}, Output={OutputDir}/{File}",
            State.AgentId, State.AuditRunId, State.OutputDir, State.CurrentFile);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TradeAudit: Run={State.AuditRunId}, Logged={State.EventsLogged}, File={State.OutputDir}/{State.CurrentFile}");
    }

    /// <summary>
    /// Configure audit behavior (called by TradingSystem during initialization).
    /// </summary>
    public void Configure(TradeAuditConfig config, string? aiModel = null)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        _includeMarketData = config.IncludeMarketData;
        _requestAiWarsUpload = config.RequestAiwarsUpload;
        _aiModel = string.IsNullOrWhiteSpace(aiModel) ? "unknown" : aiModel.Trim();
        _maxMarkdownBytes = ResolveMaxMarkdownBytes(config);

        if (!string.IsNullOrWhiteSpace(config.OutputDir))
        {
            State.OutputDir = config.OutputDir.Trim();
        }

        // Rotate file for new config/session if needed (simple strategy)
        if (string.IsNullOrWhiteSpace(State.AuditRunId))
            State.AuditRunId = Guid.NewGuid().ToString("N")[..12];

        State.CurrentFile = $"trade_audit_{State.AuditRunId}.jsonl";
        Directory.CreateDirectory(State.OutputDir);

        // Keep markdown rotation aligned with current run id / output dir.
        InitializeMarkdownRotationState();

        Logger.LogInformation(
            "[TradeAudit] Configured: IncludeMarketData={Market}, RequestAiWarsUpload={Upload}, Output={OutputDir}/{File}",
            _includeMarketData, _requestAiWarsUpload, State.OutputDir, State.CurrentFile);
    }

    [AllEventHandler(AllowSelfHandling = true)]
    public async Task HandleAnyEventAsync(EventEnvelope envelope)
    {
        // Filter: by default skip very noisy market data
        if (!_includeMarketData && IsMarketData(envelope))
            return;

        // Always attempt to build human-readable snapshot (no-throw).
        TryCacheForHumanLog(envelope);

        // Create the markdown file early (best-effort). Otherwise if the system stops before completing
        // any DecisionCycleCompletedEvent, you'll only see JSONL and think markdown is broken.
        await EnsureMarkdownInitializedAsync();

        var line = BuildJsonLine(envelope);
        await AppendLineAsync(line);

        State.EventsLogged++;
        State.LastEventTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // Optional: AI Wars UploadAiLog (generate per-cycle payload and request uploader to send it)
        if (_requestAiWarsUpload)
        {
            await MaybeRequestAiWarsUploadAsync(envelope);
        }

        // AI Wars upload observability (append to markdown)
        await TryAppendAiWarsUploadMarkdownAsync(envelope);

        // Startup guard observability (append to markdown)
        await TryAppendStartupGuardMarkdownAsync(envelope);

        // Human-readable: write a markdown block when a cycle completes.
        if (TryExtractDecisionCycleCompleted(envelope, out var completedForMd))
        {
            await AppendHumanReadableMarkdownAsync(completedForMd);
        }
    }

    private static bool IsMarketData(EventEnvelope envelope)
    {
        var typeUrl = envelope.Payload?.TypeUrl ?? string.Empty;
        return typeUrl.EndsWith(nameof(MarketTickEvent), StringComparison.Ordinal) ||
               typeUrl.EndsWith(nameof(KlineUpdateEvent), StringComparison.Ordinal);
    }

    private static bool TryExtractDecisionCycleCompleted(EventEnvelope envelope, out DecisionCycleCompletedEvent evt)
    {
        evt = new DecisionCycleCompletedEvent();
        if (envelope.Payload == null) return false;

        // Any.Unpack<T> works when type matches; otherwise throws.
        try
        {
            evt = envelope.Payload.Unpack<DecisionCycleCompletedEvent>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildJsonLine(EventEnvelope envelope)
    {
        // Convert EventEnvelope (with Any payload) to JSON.
        // IMPORTANT:
        // - We DO NOT double-encode JSON into a string field, otherwise it becomes full of \u0022 and unreadable.
        // - Instead we parse the JSON string back into JsonElement, then embed as a real JSON object.
        var envelopeJson = EnvelopeJsonFormatter.Format(envelope);

        JsonElement envelopeObj;
        try
        {
            using var doc = JsonDocument.Parse(envelopeJson);
            envelopeObj = doc.RootElement.Clone();
        }
        catch
        {
            // Fallback: keep minimal info if parsing ever fails (should be rare).
            envelopeObj = default;
        }

        JsonElement? envelopeField = envelopeObj.ValueKind == JsonValueKind.Undefined ? null : envelopeObj;

        // Wrap with audit metadata to make ingestion easier
        var wrapper = new
        {
            auditRunId = State.AuditRunId,
            auditAgentId = State.AgentId,
            envelope = envelopeField
        };

        return JsonSerializer.Serialize(wrapper, JsonOptions);
    }

    private async Task AppendLineAsync(string line)
    {
        var path = Path.Combine(State.OutputDir, State.CurrentFile);

        // Append-only file IO (robust, no long-lived handles)
        await File.AppendAllTextAsync(path, line + "\n", Utf8NoBom);
    }

    // =====================================================================
    //  Markdown summary (human readable)
    // =====================================================================

    private void TryCacheForHumanLog(EventEnvelope envelope)
    {
        if (envelope.Payload == null) return;

        // Any.Unpack<T> throws when mismatch; we keep it cheap and safe.
        try
        {
            var s = envelope.Payload.Unpack<MarketSentimentAnalysisEvent>();
            _latestSentiment[s.Symbol] = s;
        }
        catch { /* ignore */ }

        try
        {
            var t = envelope.Payload.Unpack<TechnicalAnalysisEvent>();
            _latestTechnical[t.Symbol] = t;
        }
        catch { /* ignore */ }

        try
        {
            var n = envelope.Payload.Unpack<NewsImpactAnalysisEvent>();
            // News 可以影响多个交易对：对每个 affected symbol 都缓存一份“最新新闻冲击”
            foreach (var sym in n.AffectedSymbols)
            {
                if (string.IsNullOrWhiteSpace(sym)) continue;
                _latestNews[sym] = n;
            }
        }
        catch { /* ignore */ }

        try
        {
            var started = envelope.Payload.Unpack<DecisionCycleStartedEvent>();
            _cycleStarted[started.CycleId] = started;
        }
        catch { /* ignore */ }

        try
        {
            var completed = envelope.Payload.Unpack<DecisionCycleCompletedEvent>();
            _cycleCompleted[completed.CycleId] = completed;

            // Map decisionId -> cycleId (for later AI Wars upload bundling)
            if (!string.IsNullOrWhiteSpace(completed.DecisionId))
            {
                _cycleIdByDecisionId[completed.DecisionId] = completed.CycleId;
            }
        }
        catch { /* ignore */ }

        try
        {
            var decision = envelope.Payload.Unpack<TradingDecisionEvent>();
            if (!string.IsNullOrWhiteSpace(decision.DecisionId))
                _decisionsById[decision.DecisionId] = decision;
        }
        catch { /* ignore */ }

        try
        {
            var approved = envelope.Payload.Unpack<ApprovedTradeEvent>();
            if (!string.IsNullOrWhiteSpace(approved.DecisionId))
                _approvedByDecisionId[approved.DecisionId] = approved;
        }
        catch { /* ignore */ }

        try
        {
            var rejected = envelope.Payload.Unpack<TradeRejectedEvent>();
            if (!string.IsNullOrWhiteSpace(rejected.DecisionId))
                _rejectedByDecisionId[rejected.DecisionId] = rejected;
        }
        catch { /* ignore */ }

        try
        {
            var executed = envelope.Payload.Unpack<OrderExecutedEvent>();
            if (!string.IsNullOrWhiteSpace(executed.DecisionId))
                _executedByDecisionId[executed.DecisionId] = executed;
        }
        catch { /* ignore */ }

        try
        {
            var simulated = envelope.Payload.Unpack<OrderSimulatedEvent>();
            if (!string.IsNullOrWhiteSpace(simulated.DecisionId))
                _simulatedByDecisionId[simulated.DecisionId] = simulated;
        }
        catch { /* ignore */ }

        try
        {
            var failed = envelope.Payload.Unpack<OrderFailedEvent>();
            if (!string.IsNullOrWhiteSpace(failed.DecisionId))
                _failedByDecisionId[failed.DecisionId] = failed;
        }
        catch { /* ignore */ }
    }

    // =====================================================================
    //  AI Wars UploadAiLog (自动上传触发器)
    //
    //  设计原则：
    //  - 不上传整份 JSONL（太大、重复、且不符合 uploadAiLog 参数模型）
    //  - 生成 “每个决策/结果” 的小 payload（stage/model/input/output/explanation）
    //  - 触发时机（更新后，更贴合比赛/成本/可读性）：
    //    - ✅ 仅在“真实发生交易尝试”后上传：OrderExecuted / OrderSimulated / OrderFailed
    //    - ❌ 不上传“无交易/无决策”的日志（例如 HOLD / NO_STRATEGY / 风控拒绝），这些只在本地 trade-audit 里记录
    // =====================================================================

    private async Task MaybeRequestAiWarsUploadAsync(EventEnvelope envelope)
    {
        if (envelope.Payload == null)
            return;

        // =====================================================================
        //  Trigger policy (现象 → 本质):
        //
        //  现象：远程“看起来做过决策”，但主办方收不到 AI log。
        //  本质：之前只在 OrderExecuted/Simulated/Failed 触发上传；如果决策没有走到成交事件，
        //        就永远不会上传（不会生成 artifact/receipt），外部看起来像“系统没上传”。
        //
        //  设计：只要产生了“非 HOLD 的决策周期结果”，就触发一次 upload（并去重）。
        //        这样即使下单失败/未成交，也会有 receipt 帮我们定位缺的到底是凭证/权限/网络。
        // =====================================================================

        // 1) Decision-level upload (BUY/SELL only) -> batch enqueue
        try
        {
            var completed = envelope.Payload.Unpack<DecisionCycleCompletedEvent>();
            var dir = (completed.Direction ?? "").Trim().ToUpperInvariant();
            if (dir is "BUY" or "SELL" &&
                !string.IsNullOrWhiteSpace(completed.DecisionId))
            {
                EnqueueAiWarsDecisionForBatch(completed.CycleId ?? string.Empty, completed.DecisionId);
            }
            return;
        }
        catch { /* ignore */ }

        // 2) Execution-outcome upload (best-effort; still useful when available)
        try
        {
            var executed = envelope.Payload.Unpack<OrderExecutedEvent>();
            // Batch mode: don't spam per outcome; just enqueue the decisionId so the next batch can include latest execution snapshot.
            if (!string.IsNullOrWhiteSpace(executed.DecisionId) &&
                _cycleIdByDecisionId.TryGetValue(executed.DecisionId, out var cid) &&
                !string.IsNullOrWhiteSpace(cid))
            {
                EnqueueAiWarsDecisionForBatch(cid, executed.DecisionId);
            }
            return;
        }
        catch { /* ignore */ }

        try
        {
            var simulated = envelope.Payload.Unpack<OrderSimulatedEvent>();
            if (!string.IsNullOrWhiteSpace(simulated.DecisionId) &&
                _cycleIdByDecisionId.TryGetValue(simulated.DecisionId, out var cid) &&
                !string.IsNullOrWhiteSpace(cid))
            {
                EnqueueAiWarsDecisionForBatch(cid, simulated.DecisionId);
            }
            return;
        }
        catch { /* ignore */ }

        try
        {
            var failed = envelope.Payload.Unpack<OrderFailedEvent>();
            if (!string.IsNullOrWhiteSpace(failed.DecisionId) &&
                _cycleIdByDecisionId.TryGetValue(failed.DecisionId, out var cid) &&
                !string.IsNullOrWhiteSpace(cid))
            {
                EnqueueAiWarsDecisionForBatch(cid, failed.DecisionId);
            }
        }
        catch { /* ignore */ }
    }

    private void EnqueueAiWarsDecisionForBatch(string cycleId, string decisionId)
    {
        if (string.IsNullOrWhiteSpace(decisionId))
            return;

        // 已经上传过（任何批次）就别再进队列
        if (_aiWarsUploadRequestedDecisionIds.Contains(decisionId))
            return;

        lock (_aiWarsBatchLock)
        {
            // 队列内去重
            if (_aiWarsPendingDecisionBatch.Any(x => string.Equals(x.DecisionId, decisionId, StringComparison.OrdinalIgnoreCase)))
                return;
            _aiWarsPendingDecisionBatch.Add((cycleId ?? string.Empty, decisionId, DateTime.UtcNow));
        }

        // Debounce flush：不要阻塞事件处理管线
        if (Interlocked.Exchange(ref _aiWarsBatchFlushScheduled, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(AiWarsBatchDebounceMs);
                    await FlushAiWarsDecisionBatchAsync();
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    Interlocked.Exchange(ref _aiWarsBatchFlushScheduled, 0);
                }
            });
        }
    }

    private async Task FlushAiWarsDecisionBatchAsync()
    {
        List<(string CycleId, string DecisionId, DateTime AtUtc)> batch;
        lock (_aiWarsBatchLock)
        {
            if (_aiWarsPendingDecisionBatch.Count == 0)
                return;
            batch = _aiWarsPendingDecisionBatch.ToList();
            _aiWarsPendingDecisionBatch.Clear();
        }

        // 只上传 BUY/SELL 且“不是降级/异常占位”的决策。
        // - 若本 batch 全是 fallback/熔断/异常，则不上传（避免主办方收到一堆无意义报错日志）
        var decisionIds = batch
            .Select(x => x.DecisionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id =>
            {
                if (_aiWarsUploadRequestedDecisionIds.Contains(id)) return false;
                if (!_decisionsById.TryGetValue(id, out var d) || d == null) return false;
                var dir = (d.Direction ?? "").Trim().ToUpperInvariant();
                if (dir is not ("BUY" or "SELL")) return false;
                return IsAiWarsUploadWorthyDecision(d);
            })
            .ToList();

        if (decisionIds.Count == 0)
            return;

        var payloadJson = BuildAiWarsBatchUploadPayloadJson(decisionIds);
        if (string.IsNullOrWhiteSpace(payloadJson))
            return;

        try
        {
            var dir = Path.Combine(State.OutputDir, "ai-wars");
            Directory.CreateDirectory(dir);

            var requestId = Guid.NewGuid().ToString("N")[..16];
            var fileName = $"aiwars_upload_{DateTime.UtcNow:yyyyMMddHHmmss}_{requestId}_batch_{decisionIds.Count}.json";
            var path = Path.Combine(dir, fileName);
            await File.WriteAllTextAsync(path, payloadJson, Utf8NoBom);

            foreach (var id in decisionIds)
                _aiWarsUploadRequestedDecisionIds.Add(id);

            // 用 batchId 作为 CycleId，便于 receipts/markdown 一眼辨认这是“合并上传”
            var batchId = $"batch_{DateTime.UtcNow:yyyyMMddHHmmss}_{decisionIds.Count}";

            await PublishAsync(new AiWarsLogUploadRequestedEvent
            {
                RequestId = requestId,
                CycleId = batchId,
                ArtifactPath = path,
                ContentType = "application/json",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[TradeAudit] Failed to request AI Wars batch upload");
        }
    }

    private static bool IsAiWarsUploadWorthyDecision(TradingDecisionEvent d)
    {
        if (d == null)
            return false;

        // IMPORTANT (user request update):
        // - Continue uploading AI logs even when LLM is degraded/circuit-open.
        // - BUT: remove low-level error details (exception/provider strings) from the uploaded payload.
        //
        // Therefore, we no longer block uploads here; we sanitize the payload content later.
        return true;
    }

    // ---------------------------------------------------------------------
    //  AI log sanitization (avoid leaking low-level LLM failure details)
    // ---------------------------------------------------------------------
    private static bool LooksLikeLlmDegraded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("llm-circuit-open", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("llm-timeout", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("decision-exception", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("LLMCallException", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Circuit breaker OPEN", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("fallback:", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeAiLogText(string? text, int maxLen, bool keepNewlines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var raw = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var degraded = LooksLikeLlmDegraded(raw);

        // Drop lines that leak low-level failure details.
        var lines = raw.Split('\n')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x =>
                !x.Contains("llm-circuit-open", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("llm-timeout", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("decision-exception", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("LLMCallException", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("Circuit breaker OPEN", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("fallback:", StringComparison.OrdinalIgnoreCase))
            .Take(keepNewlines ? 30 : 12)
            .ToList();

        if (degraded)
        {
            // Keep the meaning without the details.
            lines.Insert(0, "AI 降级：上游模型不可用/不稳定，已启用本地降级策略继续决策（已隐藏错误细节）。");
        }

        var joined = keepNewlines ? string.Join("\n", lines) : string.Join(" ", lines);
        joined = joined.Replace("\r", " ").Replace("\n", " ").Trim();
        while (joined.Contains("  ", StringComparison.Ordinal))
            joined = joined.Replace("  ", " ", StringComparison.Ordinal);

        if (joined.Length > maxLen)
            joined = joined[..maxLen] + "…";
        return joined;
    }

    private string BuildAiWarsBatchUploadPayloadJson(List<string> decisionIds)
    {
        if (decisionIds == null || decisionIds.Count == 0)
            return string.Empty;

        // Build per-decision entries (best-effort, all from caches)
        var entries = new List<Dictionary<string, object?>>();
        foreach (var decisionId in decisionIds)
        {
            if (!_decisionsById.TryGetValue(decisionId, out var decision) || decision == null)
                continue;

            _cycleIdByDecisionId.TryGetValue(decisionId, out var cycleId);
            DecisionCycleStartedEvent? started = null;
            DecisionCycleCompletedEvent? completed = null;
            if (!string.IsNullOrWhiteSpace(cycleId))
            {
                _cycleStarted.TryGetValue(cycleId, out started);
                _cycleCompleted.TryGetValue(cycleId, out completed);
            }

            var symbol = completed?.Symbol ?? decision.Symbol ?? started?.Symbol ?? string.Empty;
            if (string.IsNullOrWhiteSpace(symbol))
                continue;

            _latestSentiment.TryGetValue(symbol, out var sentiment);
            _latestTechnical.TryGetValue(symbol, out var technical);
            _latestNews.TryGetValue(symbol, out var news);

            _approvedByDecisionId.TryGetValue(decisionId, out var approved);
            _rejectedByDecisionId.TryGetValue(decisionId, out var rejected);
            _executedByDecisionId.TryGetValue(decisionId, out var executed);
            _simulatedByDecisionId.TryGetValue(decisionId, out var simulated);
            _failedByDecisionId.TryGetValue(decisionId, out var failed);

            var inputObj = new Dictionary<string, object?>
            {
                ["cycleId"] = cycleId,
                ["decisionId"] = decisionId,
                ["symbol"] = symbol,
                ["trigger"] = started?.Trigger,
                ["priceSnapshot"] = decision.SuggestedPrice,
                ["analysis"] = new Dictionary<string, object?>
                {
                    ["sentimentSummary"] = SanitizeAiLogText(decision.SentimentSummary ?? sentiment?.AnalysisSummary, 220, keepNewlines: false),
                    ["technicalSummary"] = SanitizeAiLogText(decision.TechnicalSummary ?? technical?.AnalysisSummary, 220, keepNewlines: false),
                    ["newsSummary"] = SanitizeAiLogText(decision.NewsSummary ?? news?.AnalysisSummary, 200, keepNewlines: false)
                }
            };

            var outputObj = new Dictionary<string, object?>
            {
                ["decision"] = new Dictionary<string, object?>
                {
                    ["direction"] = decision.Direction,
                    ["confidence"] = decision.Confidence,
                    ["positionPct"] = decision.SuggestedPositionPct,
                    ["reasoning"] = SanitizeAiLogText(decision.Reasoning, 420, keepNewlines: true)
                },
                ["risk"] = approved != null
                    ? new Dictionary<string, object?>
                    {
                        ["result"] = "APPROVED",
                        ["riskAssessment"] = approved.RiskAssessment,
                        ["positionAdjusted"] = approved.PositionSizeAdjusted,
                        ["stopLoss"] = approved.StopLoss,
                        ["takeProfit"] = approved.TakeProfit,
                        ["orderType"] = approved.OrderType,
                        ["qty"] = approved.Quantity,
                        ["price"] = approved.Price
                    }
                    : rejected != null
                        ? new Dictionary<string, object?>
                        {
                            ["result"] = "REJECTED",
                            ["riskLevel"] = rejected.RiskLevel,
                            ["reason"] = rejected.RejectionReason,
                            ["violatedRules"] = rejected.ViolatedRules.ToArray()
                        }
                        : null,
                ["execution"] = executed != null
                    ? new Dictionary<string, object?>
                    {
                        ["result"] = "ORDER_EXECUTED",
                        ["orderId"] = executed.OrderId,
                        ["clientOrderId"] = executed.ClientOrderId,
                        ["status"] = executed.Status
                    }
                    : simulated != null
                        ? new Dictionary<string, object?>
                        {
                            ["result"] = "ORDER_SIMULATED",
                            ["clientOrderId"] = simulated.ClientOrderId,
                            ["reason"] = simulated.Reason
                        }
                        : failed != null
                            ? new Dictionary<string, object?>
                            {
                                ["result"] = "ORDER_FAILED",
                                ["errorCode"] = failed.ErrorCode,
                                ["errorMessage"] = failed.ErrorMessage
                            }
                            : null
            };

            entries.Add(new Dictionary<string, object?>
            {
                ["symbol"] = symbol,
                ["input"] = inputObj,
                ["output"] = outputObj
            });
        }

        if (entries.Count == 0)
            return string.Empty;

        var explanation = BuildAiWarsBatchExplanation(entries);

        var payload = new Dictionary<string, object?>
        {
            // 多币对 batch：可能包含多个订单，因此 orderId 置空
            ["orderId"] = null,
            ["stage"] = "Decision Batch",
            ["model"] = _aiModel,
            ["input"] = new Dictionary<string, object?>
            {
                ["batchSize"] = entries.Count,
                ["decisions"] = entries.Select(x => x["input"]).ToArray()
            },
            ["output"] = new Dictionary<string, object?>
            {
                ["results"] = entries.Select(x => x["output"]).ToArray()
            },
            ["explanation"] = explanation
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });
    }

    private static string BuildAiWarsBatchExplanation(List<Dictionary<string, object?>> entries)
    {
        // Batch explanation = “本轮汇总”，要短、可读、可审计（<=1000 chars）
        // - 多行输出：一行一个币对，便于人类扫读
        var sb = new StringBuilder();
        sb.AppendLine($"【合并上传汇总】本轮 {entries.Count} 个币对决策（batch upload）");

        static string GetNestedString(Dictionary<string, object?>? obj, string key)
        {
            if (obj == null) return "";
            return obj.TryGetValue(key, out var v) ? (v?.ToString() ?? "") : "";
        }

        static Dictionary<string, object?>? GetNestedObj(Dictionary<string, object?>? obj, string key)
        {
            if (obj == null) return null;
            return obj.TryGetValue(key, out var v) ? (v as Dictionary<string, object?>) : null;
        }

        var i = 1;
        foreach (var it in entries)
        {
            var input = it.TryGetValue("input", out var inp) ? inp as Dictionary<string, object?> : null;
            var output = it.TryGetValue("output", out var outp) ? outp as Dictionary<string, object?> : null;
            var symbol = input != null && input.TryGetValue("symbol", out var sym) ? (sym?.ToString() ?? "UNKNOWN") : "UNKNOWN";

            var decisionObj = output != null && output.TryGetValue("decision", out var d) ? d as Dictionary<string, object?> : null;
            var riskObj = output != null && output.TryGetValue("risk", out var r) ? r as Dictionary<string, object?> : null;
            var execObj = output != null && output.TryGetValue("execution", out var e) ? e as Dictionary<string, object?> : null;

            var dir = decisionObj != null && decisionObj.TryGetValue("direction", out var dv) ? (dv?.ToString() ?? "HOLD") : "HOLD";
            var conf = decisionObj != null && decisionObj.TryGetValue("confidence", out var cv) ? (cv?.ToString() ?? "0") : "0";
            var pos = decisionObj != null && decisionObj.TryGetValue("positionPct", out var pv) ? (pv?.ToString() ?? "0") : "0";

            var risk = riskObj != null && riskObj.TryGetValue("result", out var rr) ? (rr?.ToString() ?? "UNKNOWN") : "UNKNOWN";
            var exec = execObj != null && execObj.TryGetValue("result", out var er) ? (er?.ToString() ?? "NO_ORDER") : "NO_ORDER";

            sb.AppendLine($"{i}) {symbol}  {dir}  conf={conf}  pos={pos}%  风控={risk}  执行={exec}");

            // 市场分析（摘要）：来自 input.analysis.*
            var analysis = GetNestedObj(input, "analysis");
            var tech = TrimOneLine(GetNestedString(analysis, "technicalSummary"), 120);
            var senti = TrimOneLine(GetNestedString(analysis, "sentimentSummary"), 120);
            var news = TrimOneLine(GetNestedString(analysis, "newsSummary"), 90);
            sb.AppendLine("   市场分析：");
            sb.AppendLine($"   - 技术：{(!string.IsNullOrWhiteSpace(tech) ? tech : "N/A")}");
            sb.AppendLine($"   - 情绪：{(!string.IsNullOrWhiteSpace(senti) ? senti : "N/A")}");
            sb.AppendLine($"   - 新闻：{(!string.IsNullOrWhiteSpace(news) ? news : "N/A")}");

            // 决策过程（摘要）：来自 output.decision.reasoning（可能包含多行）
            var reasoning = TrimPretty(GetNestedString(decisionObj, "reasoning"), 260);
            sb.AppendLine("   决策过程：");
            if (!string.IsNullOrWhiteSpace(reasoning))
                sb.AppendLine($"   {TrimOneLine(reasoning, 320)}");
            else
                sb.AppendLine("   N/A");

            i++;

            // Keep some headroom for trailing trimming.
            if (sb.Length >= 940)
                break;
        }

        var text = sb.ToString().Trim();
        if (text.Length > 1000)
            text = text[..1000];
        return text;
    }

    private async Task RequestAiWarsUploadAsync(
        string cycleId,
        string decisionId,
        string stage,
        long? orderId)
    {
        // 去重：一个 decision 只触发一次上传（避免同时命中多个事件时重复）
        if (!string.IsNullOrWhiteSpace(decisionId) && _aiWarsUploadRequestedDecisionIds.Contains(decisionId))
            return;

        var payloadJson = BuildAiWarsUploadPayloadJson(cycleId, decisionId, stage, orderId);
        if (string.IsNullOrWhiteSpace(payloadJson))
            return;

        try
        {
            var dir = Path.Combine(State.OutputDir, "ai-wars");
            Directory.CreateDirectory(dir);

            var safeDecision = string.IsNullOrWhiteSpace(decisionId) ? "no_decision" : decisionId;
            var requestId = Guid.NewGuid().ToString("N")[..16];
            var fileName = $"aiwars_upload_{DateTime.UtcNow:yyyyMMddHHmmss}_{requestId}_{safeDecision}.json";
            var path = Path.Combine(dir, fileName);
            await File.WriteAllTextAsync(path, payloadJson, Utf8NoBom);

            if (!string.IsNullOrWhiteSpace(decisionId))
                _aiWarsUploadRequestedDecisionIds.Add(decisionId);

            await PublishAsync(new AiWarsLogUploadRequestedEvent
            {
                RequestId = requestId,
                CycleId = cycleId ?? string.Empty,
                ArtifactPath = path,
                ContentType = "application/json",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[TradeAudit] Failed to request AI Wars upload");
        }
    }

    private string BuildAiWarsUploadPayloadJson(
        string cycleId,
        string decisionId,
        string stage,
        long? orderId)
    {
        // Build from cached events (best-effort)
        _decisionsById.TryGetValue(decisionId, out var decision);
        if (decision == null)
            return string.Empty;

        // Continue uploading even when degraded; sanitize later.
        if (!IsAiWarsUploadWorthyDecision(decision))
            return string.Empty;

        DecisionCycleStartedEvent? started = null;
        DecisionCycleCompletedEvent? completed = null;
        if (!string.IsNullOrWhiteSpace(cycleId))
        {
            _cycleStarted.TryGetValue(cycleId, out started);
            _cycleCompleted.TryGetValue(cycleId, out completed);
        }

        var symbol = completed?.Symbol ?? decision?.Symbol ?? started?.Symbol ?? string.Empty;
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;

        _latestSentiment.TryGetValue(symbol, out var sentiment);
        _latestTechnical.TryGetValue(symbol, out var technical);
        _latestNews.TryGetValue(symbol, out var news);

        _approvedByDecisionId.TryGetValue(decisionId, out var approved);
        _rejectedByDecisionId.TryGetValue(decisionId, out var rejected);
        _executedByDecisionId.TryGetValue(decisionId, out var executed);
        _simulatedByDecisionId.TryGetValue(decisionId, out var simulated);
        _failedByDecisionId.TryGetValue(decisionId, out var failed);

        // -------------------------------
        // input: what AI "saw"
        // -------------------------------
        var inputObj = new Dictionary<string, object?>
        {
            ["cycleId"] = cycleId,
            ["decisionId"] = decisionId,
            ["symbol"] = symbol,
            ["trigger"] = started?.Trigger,
            ["priceSnapshot"] = decision?.SuggestedPrice ?? 0d,
            ["analysis"] = new Dictionary<string, object?>
            {
                ["sentimentSummary"] = SanitizeAiLogText(decision?.SentimentSummary ?? sentiment?.AnalysisSummary, 220, keepNewlines: false),
                ["technicalSummary"] = SanitizeAiLogText(decision?.TechnicalSummary ?? technical?.AnalysisSummary, 220, keepNewlines: false),
                ["newsSummary"] = SanitizeAiLogText(decision?.NewsSummary ?? news?.AnalysisSummary, 200, keepNewlines: false)
            }
        };

        // -------------------------------
        // output: what AI "produced"
        // -------------------------------
        var outputObj = new Dictionary<string, object?>
        {
            ["decision"] = decision == null ? null : new Dictionary<string, object?>
            {
                ["direction"] = decision.Direction,
                ["confidence"] = decision.Confidence,
                ["positionPct"] = decision.SuggestedPositionPct,
                ["reasoning"] = SanitizeAiLogText(decision.Reasoning, 420, keepNewlines: true)
            },
            ["risk"] = approved != null
                ? new Dictionary<string, object?>
                {
                    ["result"] = "APPROVED",
                    ["riskAssessment"] = approved.RiskAssessment,
                    ["positionAdjusted"] = approved.PositionSizeAdjusted,
                    ["orderType"] = approved.OrderType,
                    ["qty"] = approved.Quantity,
                    ["price"] = approved.Price
                }
                : rejected != null
                    ? new Dictionary<string, object?>
                    {
                        ["result"] = "REJECTED",
                        ["riskLevel"] = rejected.RiskLevel,
                        ["reason"] = rejected.RejectionReason,
                        ["violatedRules"] = rejected.ViolatedRules.ToArray()
                    }
                    : null,
            ["execution"] = executed != null
                ? new Dictionary<string, object?>
                {
                    ["result"] = "ORDER_EXECUTED",
                    ["orderId"] = executed.OrderId,
                    ["clientOrderId"] = executed.ClientOrderId,
                    ["status"] = executed.Status
                }
                : simulated != null
                    ? new Dictionary<string, object?>
                    {
                        ["result"] = "ORDER_SIMULATED",
                        ["clientOrderId"] = simulated.ClientOrderId,
                        ["reason"] = simulated.Reason
                    }
                    : failed != null
                        ? new Dictionary<string, object?>
                        {
                            ["result"] = "ORDER_FAILED",
                            ["errorCode"] = failed.ErrorCode,
                            ["errorMessage"] = failed.ErrorMessage
                        }
                        : null
        };

        var explanation = BuildAiWarsExplanation(decision, approved, rejected, executed, simulated, failed);

        var payload = new Dictionary<string, object?>
        {
            ["orderId"] = orderId,
            ["stage"] = stage,
            ["model"] = _aiModel,
            ["input"] = inputObj,
            ["output"] = outputObj,
            ["explanation"] = explanation
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });
    }

    private static string BuildAiWarsExplanation(
        TradingDecisionEvent? decision,
        ApprovedTradeEvent? approved,
        TradeRejectedEvent? rejected,
        OrderExecutedEvent? executed,
        OrderSimulatedEvent? simulated,
        OrderFailedEvent? failed)
    {
        // ============================================================
        //  AI Wars "explanation"（小作文）可读性目标：
        //  - 必须 <= 1000 chars（API 限制）
        //  - 必须可扫描：分段、换行、关键数字对齐
        //  - 必须可审计：说明信号 -> 决策 -> 风控 -> 执行 -> 异常/降级
        // ============================================================

        var symbol = (decision?.Symbol ?? "").Trim();
        var dir = ((decision?.Direction ?? "HOLD").Trim()).ToUpperInvariant();
        var conf = decision?.Confidence ?? 0;
        var posPct = decision?.SuggestedPositionPct ?? 0;
        var px = decision?.SuggestedPrice ?? 0d;

        var s = decision?.SentimentSummary ?? "";
        var t = decision?.TechnicalSummary ?? "";
        var n = decision?.NewsSummary ?? "";
        var whyDecision = decision?.Reasoning ?? "";

        // Detect degradation, but do NOT include low-level exception details in uploaded explanation.
        var degraded = LooksLikeLlmDegraded(whyDecision);

        static string F(double v, string fmt) => v > 0 ? v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture) : "-";
        static string F6(double v) => v > 0 ? v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "-";

        // ------------------------------------------------------------
        //  分段小作文（多行）
        // ------------------------------------------------------------
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【交易复盘】{(string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol)}");
        sb.AppendLine($"- 决策：{dir}（置信度 {conf}）  仓位建议：{(posPct > 0 ? $"{posPct:F2}%" : "-")}  参考价：{F6(px)}");
        sb.AppendLine();

        // 信号摘要（每行一类）
        sb.AppendLine("【信号摘要】");
        if (!string.IsNullOrWhiteSpace(t))
            sb.AppendLine($"- 技术：{TrimOneLine(t, 220)}");
        else
            sb.AppendLine("- 技术：N/A");

        if (!string.IsNullOrWhiteSpace(s))
            sb.AppendLine($"- 情绪：{TrimOneLine(s, 220)}");
        else
            sb.AppendLine("- 情绪：N/A");

        if (!string.IsNullOrWhiteSpace(n))
            sb.AppendLine($"- 新闻：{TrimOneLine(n, 180)}");
        else
            sb.AppendLine("- 新闻：N/A");
        sb.AppendLine();

        // 风控（明确 SL/TP/仓位调整）
        sb.AppendLine("【风控】");
        if (approved != null)
        {
            sb.AppendLine($"- 结论：通过（等级={approved.RiskAssessment}）");
            sb.AppendLine($"- 调整仓位：{approved.PositionSizeAdjusted:F2}%  订单：{approved.OrderType}  qty={F(approved.Quantity, "F6")}  price={F6(approved.Price)}");
            sb.AppendLine($"- 止损：{F6(approved.StopLoss)}  止盈：{F6(approved.TakeProfit)}");
        }
        else if (rejected != null)
        {
            var why = rejected.RejectionReason ?? "";
            if (why.Length > 240) why = why[..240] + "…";
            sb.AppendLine($"- 结论：拒绝（等级={rejected.RiskLevel}）");
            sb.AppendLine($"- 原因：{TrimOneLine(why, 260)}");
        }
        else
        {
            sb.AppendLine("- 结论：未知（缺少风控事件）");
        }
        sb.AppendLine();

        // 执行
        sb.AppendLine("【执行】");
        if (executed != null)
        {
            sb.AppendLine($"- 结果：ORDER_EXECUTED  orderId={executed.OrderId}  status={executed.Status}");
        }
        else if (simulated != null)
        {
            var r = simulated.Reason ?? "";
            if (r.Length > 240) r = r[..240] + "…";
            sb.AppendLine("- 结果：ORDER_SIMULATED");
            sb.AppendLine($"- 原因：{TrimOneLine(r, 260)}");
        }
        else if (failed != null)
        {
            var msg = failed.ErrorMessage ?? "";
            if (msg.Length > 240) msg = msg[..240] + "…";
            sb.AppendLine($"- 结果：ORDER_FAILED  code={failed.ErrorCode}");
            sb.AppendLine($"- 信息：{TrimOneLine(msg, 260)}");
        }
        else
        {
            sb.AppendLine("- 结果：无订单（本周期未进入执行/或执行事件缺失）");
        }
        sb.AppendLine();

        // 决策过程 / 降级说明
        sb.AppendLine("【决策过程】");
        if (degraded)
        {
            sb.AppendLine("- 状态：AI 降级（上游模型不可用/不稳定）");
            sb.AppendLine("- 说明：已启用本地降级策略继续决策（已隐藏错误细节）。");
        }
        else if (!string.IsNullOrWhiteSpace(whyDecision))
        {
            // Keep readability, but also sanitize in case provider details slipped into reasoning.
            sb.AppendLine(SanitizeAiLogText(whyDecision, 420, keepNewlines: true));
        }
        else
        {
            sb.AppendLine("- 说明：未提供额外 reasoning（可能是规则/解析缺失）");
        }

        var text = sb.ToString().Trim();
        if (text.Length > 1000)
            text = text[..1000];
        return text;
    }

    private static string TrimPretty(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        // Keep newlines (readability), but normalize excessive blank lines and trim each line.
        var raw = s.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = raw.Split('\n')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(20)
            .ToArray();

        var joined = string.Join("\n", lines);
        if (joined.Length > maxLen)
            joined = joined[..maxLen] + "…";
        return joined;
    }

    private static string TrimOneLine(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var x = s.Replace("\r", " ").Replace("\n", " ").Trim();
        while (x.Contains("  ", StringComparison.Ordinal))
            x = x.Replace("  ", " ", StringComparison.Ordinal);
        if (x.Length > maxLen)
            x = x[..maxLen] + "…";
        return x;
    }

    private static long? TryParseLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return long.TryParse(value.Trim(), out var l) ? l : null;
    }

    private async Task EnsureMarkdownInitializedAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(State.OutputDir))
                State.OutputDir = "trade-audit";
            if (string.IsNullOrWhiteSpace(_markdownFile))
                _markdownFile = BuildMarkdownActiveFileName();

            Directory.CreateDirectory(State.OutputDir);

            var mdPath = Path.Combine(State.OutputDir, _markdownFile);
            if (File.Exists(mdPath))
            {
                return;
            }

            var header = BuildMarkdownHeader();
            await File.WriteAllTextAsync(mdPath, header, Utf8NoBom);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[TradeAudit] Failed to initialize markdown log file");
        }
    }

    private void InitializeMarkdownRotationState()
    {
        // Active file is stable; parts are archived with _partNNNN suffix.
        _markdownFile = BuildMarkdownActiveFileName();

        // Best-effort: detect next part index from existing files, so restart continues cleanly.
        _nextMarkdownPartIndex = DetectNextMarkdownPartIndex();
    }

    private string BuildMarkdownActiveFileName()
        => $"trade_audit_{State.AuditRunId}.md";

    private string BuildMarkdownPartFileName(int partIndex)
        => $"trade_audit_{State.AuditRunId}_part{partIndex:0000}.md";

    private int DetectNextMarkdownPartIndex()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(State.OutputDir))
                return 1;
            if (!Directory.Exists(State.OutputDir))
                return 1;
            if (string.IsNullOrWhiteSpace(State.AuditRunId))
                return 1;

            // trade_audit_<runId>_partNNNN.md
            var prefix = $"trade_audit_{State.AuditRunId}_part";
            var max = 0;
            foreach (var path in Directory.EnumerateFiles(State.OutputDir, $"trade_audit_{State.AuditRunId}_part*.md",
                         SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var numStr = name[prefix.Length..];
                if (numStr.Length == 0)
                    continue;

                if (int.TryParse(numStr, out var n) && n > max)
                    max = n;
            }

            return max + 1;
        }
        catch
        {
            return 1;
        }
    }

    private static int ResolveMaxMarkdownBytes(TradeAuditConfig config)
    {
        // Protobuf default is 0; interpret as "use code default".
        // Allow <0 to disable rotation explicitly.
        if (config.MaxMarkdownBytes < 0)
            return 0;
        if (config.MaxMarkdownBytes > 0)
            return config.MaxMarkdownBytes;
        return DefaultMaxMarkdownBytes;
    }

    private async Task<string> GetMarkdownPathForAppendAsync(string upcomingBlock)
    {
        // Ensure active file exists (header).
        await EnsureMarkdownInitializedAsync();

        if (_maxMarkdownBytes <= 0)
            return Path.Combine(State.OutputDir, _markdownFile);

        try
        {
            var activePath = Path.Combine(State.OutputDir, _markdownFile);
            var fi = new FileInfo(activePath);
            if (!fi.Exists)
                return activePath;

            var upcomingBytes = Utf8NoBom.GetByteCount(upcomingBlock);
            if (fi.Length + upcomingBytes <= _maxMarkdownBytes)
                return activePath;

            // Rotate: trade_audit_<runId>.md -> trade_audit_<runId>_partNNNN.md
            while (true)
            {
                var partName = BuildMarkdownPartFileName(_nextMarkdownPartIndex);
                var partPath = Path.Combine(State.OutputDir, partName);
                if (File.Exists(partPath))
                {
                    _nextMarkdownPartIndex++;
                    continue;
                }

                File.Move(activePath, partPath);
                _nextMarkdownPartIndex++;
                break;
            }

            // Create fresh active file with header.
            await EnsureMarkdownInitializedAsync();
        }
        catch (Exception ex)
        {
            // Never fail the trading loop because audit rotation failed.
            Logger.LogWarning(ex, "[TradeAudit] Markdown rotation failed; continuing without rotation");
        }

        return Path.Combine(State.OutputDir, _markdownFile);
    }

    private async Task TryAppendAiWarsUploadMarkdownAsync(EventEnvelope envelope)
    {
        if (envelope.Payload == null)
            return;

        // Requested
        try
        {
            var req = envelope.Payload.Unpack<AiWarsLogUploadRequestedEvent>();
            await AppendAiWarsUploadMarkdownBlockAsync(
                status: "REQUESTED",
                reqId: req.RequestId,
                cycleId: req.CycleId,
                detail: $"artifact=`{ToRelativeAuditPath(req.ArtifactPath)}`");
            return;
        }
        catch { /* ignore */ }

        // Succeeded
        try
        {
            var ok = envelope.Payload.Unpack<AiWarsLogUploadSucceededEvent>();
            var receiptRel = $"ai-wars/receipts/aiwars_receipt_{ok.RequestId}.json";
            await AppendAiWarsUploadMarkdownBlockAsync(
                status: "SUCCESS",
                reqId: ok.RequestId,
                cycleId: ok.CycleId,
                detail: $"receipt=`{receiptRel}`");
            return;
        }
        catch { /* ignore */ }

        // Failed
        try
        {
            var fail = envelope.Payload.Unpack<AiWarsLogUploadFailedEvent>();
            var receiptRel = $"ai-wars/receipts/aiwars_receipt_{fail.RequestId}.json";
            await AppendAiWarsUploadMarkdownBlockAsync(
                status: $"FAILED({fail.ErrorCode})",
                reqId: fail.RequestId,
                cycleId: fail.CycleId,
                detail: $"msg={SanitizeOneLine(fail.ErrorMessage)}; receipt=`{receiptRel}`");
        }
        catch { /* ignore */ }
    }

    private async Task AppendAiWarsUploadMarkdownBlockAsync(string status, string reqId, string cycleId, string detail)
    {
        try
        {
            var now = DateTime.UtcNow;
            var block = $"""

### AI Wars Upload

- Time(UTC): `{now:O}`
- Status: **{status}**
- RequestId: `{reqId}`
- CycleId: `{cycleId}`
- Detail: {detail}

""";
            var mdPath = await GetMarkdownPathForAppendAsync(block);
            await File.AppendAllTextAsync(mdPath, block, Utf8NoBom);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[TradeAudit] Failed to write AI Wars upload markdown block");
        }
    }

    private async Task TryAppendStartupGuardMarkdownAsync(EventEnvelope envelope)
    {
        if (envelope.Payload == null)
            return;

        // Order submitted (we use OrderExecutedEvent as "submitted" in this system)
        try
        {
            var exec = envelope.Payload.Unpack<OrderExecutedEvent>();
            if (!IsStartupGuard(exec.ClientOrderId, exec.DecisionId))
                return;
            var now = DateTime.UtcNow;

            var block = $"""

### Startup Guard

- Time(UTC): `{now:O}`
- Action: **AUTO BUY** (min base asset value)
- Symbol: `{exec.Symbol}`
- Qty: `{exec.Quantity}`
- PriceSnapshot: `{exec.FilledPrice}`
- OrderId: `{exec.OrderId}`
- ClientOrderId: `{exec.ClientOrderId}`
- Status: `{exec.Status}`

""";
            var mdPath = await GetMarkdownPathForAppendAsync(block);
            await File.AppendAllTextAsync(mdPath, block, Utf8NoBom);
            return;
        }
        catch { /* ignore */ }

        // Order failed
        try
        {
            var fail = envelope.Payload.Unpack<OrderFailedEvent>();
            if (!IsStartupGuard(fail.ClientOrderId, fail.DecisionId))
                return;
            var now = DateTime.UtcNow;

            var block = $"""

### Startup Guard

- Time(UTC): `{now:O}`
- Action: **AUTO BUY** (min base asset value)
- Symbol: `{fail.Symbol}`
- ClientOrderId: `{fail.ClientOrderId}`
- Status: **FAILED({fail.ErrorCode})**
- Message: {SanitizeOneLine(fail.ErrorMessage)}

""";
            var mdPath = await GetMarkdownPathForAppendAsync(block);
            await File.AppendAllTextAsync(mdPath, block, Utf8NoBom);
        }
        catch { /* ignore */ }
    }

    private static bool IsStartupGuard(string? clientOrderId, string? decisionId)
    {
        if (!string.IsNullOrWhiteSpace(decisionId) &&
            string.Equals(decisionId.Trim(), "BOOTSTRAP_GUARD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(clientOrderId) &&
            clientOrderId.Trim().StartsWith("BOOTSTRAP_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string SanitizeOneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var s = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length > 220)
            s = s[..220] + "…";
        return s;
    }

    private static string ToRelativeAuditPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        // Keep output readable in markdown (prefer path under "trade-audit/" when possible).
        var p = path.Replace("\\", "/");
        var idx = p.LastIndexOf("/trade-audit/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return p[(idx + 1)..]; // keep "trade-audit/..."
        return p;
    }

    private async Task AppendHumanReadableMarkdownAsync(DecisionCycleCompletedEvent completed)
    {
        try
        {
            var block = BuildCycleMarkdown(completed);
            var mdPath = await GetMarkdownPathForAppendAsync(block);
            await File.AppendAllTextAsync(mdPath, block, Utf8NoBom);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[TradeAudit] Failed to write markdown summary");
        }
    }

    private string BuildMarkdownHeader()
    {
        var now = DateTime.UtcNow;
        var jsonl = Path.Combine(State.OutputDir, State.CurrentFile).Replace("\\", "/");
        var partsHint = $"trade_audit_{State.AuditRunId}_part*.md";
        return $"""
# Trade Strategy Log (Human Readable)

- Run: `{State.AuditRunId}`
- GeneratedAt(UTC): `{now:O}`
- JSONL Artifact: `{jsonl}`
- ArchiveParts: `{partsHint}` (auto-rotated when too large)

> 说明：每个 Decision Cycle 会追加一段摘要（AI 分析 → 决策 → 风控 → 执行结果），用于比赛 Demo/复盘。

---

""";
    }

    private string BuildCycleMarkdown(DecisionCycleCompletedEvent completed)
    {
        var cycleId = completed.CycleId ?? "";
        var symbol = completed.Symbol ?? "";
        var decisionId = completed.DecisionId ?? "";

        _cycleStarted.TryGetValue(cycleId, out var started);
        _decisionsById.TryGetValue(decisionId, out var decision);
        _approvedByDecisionId.TryGetValue(decisionId, out var approved);
        _rejectedByDecisionId.TryGetValue(decisionId, out var rejected);
        _executedByDecisionId.TryGetValue(decisionId, out var executed);
        _simulatedByDecisionId.TryGetValue(decisionId, out var simulated);
        _failedByDecisionId.TryGetValue(decisionId, out var failed);

        // Enrich: latest analysis snapshot
        _latestSentiment.TryGetValue(symbol, out var sentiment);
        _latestTechnical.TryGetValue(symbol, out var technical);
        _latestNews.TryGetValue(symbol, out var news);

        var startTime = started?.Timestamp?.ToDateTime();
        var endTime = completed.Timestamp.ToDateTime();

        var executedFlag = completed.Executed ? "YES" : "NO";
        var mode = completed.ExecutionMode ?? "";

        var dir = completed.Direction ?? decision?.Direction ?? "HOLD";
        var conf = completed.Confidence > 0 ? completed.Confidence : (decision?.Confidence ?? 0);

        var aiReason = decision?.Reasoning ?? "";
        var sentSum = decision?.SentimentSummary ?? "";
        var techSum = decision?.TechnicalSummary ?? "";
        var newsSum = decision?.NewsSummary ?? "";

        // ------------------------------------------------------------
        //  AI Used marker (critical observability)
        //
        //  - YES: decision came from an AI engine (Direct LLM / CognitiveMesh, etc.)
        //  - NO : decision came from deterministic fallback policy (DecisionId starts with "FALLBACK_")
        // ------------------------------------------------------------
        var aiUsed = !string.IsNullOrWhiteSpace(decisionId) &&
                     !decisionId.StartsWith("FALLBACK_", StringComparison.OrdinalIgnoreCase);

        var riskLine = approved != null
            ? $"APPROVED ({approved.RiskAssessment})"
            : rejected != null
                ? $"REJECTED ({rejected.RiskLevel})"
                : "UNKNOWN";

        var execLine = executed != null
            ? $"ORDER_EXECUTED: orderId={executed.OrderId}, qty={executed.Quantity}, price={executed.FilledPrice}, status={executed.Status}"
            : simulated != null
                ? $"ORDER_SIMULATED: simId={simulated.SimulatedOrderId}, qty={simulated.Quantity}, price={simulated.Price}, reason={simulated.Reason}"
                : failed != null
                    ? $"ORDER_FAILED: code={failed.ErrorCode}, msg={failed.ErrorMessage}"
                    : "NO_ORDER_EVENT";

        // Human-friendly metrics (optional)
        var sentimentLine = sentiment != null
            ? $"score={sentiment.SentimentScore}, trend={sentiment.SentimentTrend}, funding={sentiment.FundingRate}, oi={sentiment.OpenInterest}"
            : "N/A";

        var technicalLine = technical != null
            ? $"trend={technical.TrendDirection}, signal={technical.Signal}, rsi={technical.Rsi:F2}, macd={technical.Macd:F4}"
            : "N/A";

        var sb = new StringBuilder();
        sb.AppendLine($"## Cycle `{cycleId}` — `{symbol}`");
        sb.AppendLine();
        sb.AppendLine($"- Time(UTC): `{(startTime.HasValue ? startTime.Value.ToString("O") : "")}` → `{endTime:O}`");
        sb.AppendLine($"- Trigger: `{started?.Trigger ?? ""}`");
        sb.AppendLine($"- Decision: **{dir}** (confidence={conf})");
        sb.AppendLine($"- DecisionId: `{decisionId}`");
        sb.AppendLine($"- AI Used: `{(aiUsed ? "YES" : "NO")}`");
        sb.AppendLine($"- ExecutedToRisk: `{executedFlag}` | Mode: `{mode}`");
        sb.AppendLine();

        sb.AppendLine("### AI Strategy (可读摘要)");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(sentSum)) sb.AppendLine($"- Sentiment: {sentSum}");
        if (!string.IsNullOrWhiteSpace(techSum)) sb.AppendLine($"- Technical: {techSum}");
        if (!string.IsNullOrWhiteSpace(newsSum)) sb.AppendLine($"- News: {newsSum}");
        if (!string.IsNullOrWhiteSpace(aiReason)) sb.AppendLine($"- Reasoning: {aiReason}");
        if (string.IsNullOrWhiteSpace(sentSum) && string.IsNullOrWhiteSpace(techSum) && string.IsNullOrWhiteSpace(newsSum) && string.IsNullOrWhiteSpace(aiReason))
            sb.AppendLine("- (no decision content captured)");
        sb.AppendLine();

        sb.AppendLine("### Signal Snapshot (用于复盘)");
        sb.AppendLine();
        sb.AppendLine($"- Sentiment: {sentimentLine}");
        sb.AppendLine($"- Technical: {technicalLine}");
        if (news != null)
        {
            sb.AppendLine($"- News: impact={news.ImpactType}/{news.ImpactLevel}, headline={news.Headline}");
        }
        sb.AppendLine();

        sb.AppendLine("### Risk Control");
        sb.AppendLine();
        sb.AppendLine($"- Result: {riskLine}");
        if (approved != null)
        {
            sb.AppendLine($"- Order: type={approved.OrderType}, qty={approved.Quantity}, price={approved.Price}");
            sb.AppendLine($"- SL/TP: stop={approved.StopLoss}, take={approved.TakeProfit}");
            sb.AppendLine($"- Notes: {approved.RiskNotes}");
        }
        if (rejected != null)
        {
            sb.AppendLine($"- Reason: {rejected.RejectionReason}");
            if (rejected.ViolatedRules.Count > 0)
                sb.AppendLine($"- Violations: {string.Join(", ", rejected.ViolatedRules)}");
        }
        sb.AppendLine();

        sb.AppendLine("### Execution Result");
        sb.AppendLine();
        sb.AppendLine($"- {execLine}");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        return sb.ToString();
    }
}


