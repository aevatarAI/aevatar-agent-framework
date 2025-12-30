using System.Text;
using System.Text.Json;
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
        await File.AppendAllTextAsync(path, line + "\n", Encoding.UTF8);
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

        // Only upload when an order was attempted / produced an execution outcome.
        try
        {
            var executed = envelope.Payload.Unpack<OrderExecutedEvent>();
            await RequestAiWarsUploadAsync(
                cycleId: _cycleIdByDecisionId.TryGetValue(executed.DecisionId, out var cid) ? cid : string.Empty,
                decisionId: executed.DecisionId,
                stage: "Order Execution",
                orderId: TryParseLong(executed.OrderId));
            return;
        }
        catch { /* ignore */ }

        try
        {
            var simulated = envelope.Payload.Unpack<OrderSimulatedEvent>();
            await RequestAiWarsUploadAsync(
                cycleId: _cycleIdByDecisionId.TryGetValue(simulated.DecisionId, out var cid) ? cid : string.Empty,
                decisionId: simulated.DecisionId,
                stage: "Order Simulation",
                orderId: null);
            return;
        }
        catch { /* ignore */ }

        try
        {
            var failed = envelope.Payload.Unpack<OrderFailedEvent>();
            await RequestAiWarsUploadAsync(
                cycleId: _cycleIdByDecisionId.TryGetValue(failed.DecisionId, out var cid) ? cid : string.Empty,
                decisionId: failed.DecisionId,
                stage: "Order Failure",
                orderId: null);
        }
        catch { /* ignore */ }
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
            await File.WriteAllTextAsync(path, payloadJson, Encoding.UTF8);

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
                ["sentimentSummary"] = decision?.SentimentSummary ?? sentiment?.AnalysisSummary,
                ["technicalSummary"] = decision?.TechnicalSummary ?? technical?.AnalysisSummary,
                ["newsSummary"] = decision?.NewsSummary ?? news?.AnalysisSummary
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
                ["reasoning"] = decision.Reasoning
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
        var dir = decision?.Direction ?? "HOLD";
        var conf = decision?.Confidence ?? 0;

        var risk = approved != null
            ? $"APPROVED({approved.RiskAssessment})"
            : rejected != null
                ? $"REJECTED({rejected.RiskLevel})"
                : "UNKNOWN";

        var exec = executed != null
            ? $"ORDER_EXECUTED(orderId={executed.OrderId})"
            : simulated != null
                ? "ORDER_SIMULATED"
                : failed != null
                    ? $"ORDER_FAILED({failed.ErrorCode})"
                    : "NO_ORDER";

        var reason = decision?.Reasoning ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(reason) && reason.Length > 600)
            reason = reason[..600] + "…";

        var text = $"Decision={dir} (conf={conf}); Risk={risk}; Exec={exec}. {reason}".Trim();
        if (text.Length > 1000)
            text = text[..1000];
        return text;
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
            await File.WriteAllTextAsync(mdPath, header, Encoding.UTF8);
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

            var upcomingBytes = Encoding.UTF8.GetByteCount(upcomingBlock);
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
            await File.AppendAllTextAsync(mdPath, block, Encoding.UTF8);
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
            await File.AppendAllTextAsync(mdPath, block, Encoding.UTF8);
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
            await File.AppendAllTextAsync(mdPath, block, Encoding.UTF8);
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
            await File.AppendAllTextAsync(mdPath, block, Encoding.UTF8);
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


