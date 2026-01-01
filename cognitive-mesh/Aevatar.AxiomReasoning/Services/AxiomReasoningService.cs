using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AGUI;
using Aevatar.AxiomReasoning.EventStreaming.AgUi;
using Aevatar.AxiomReasoning.Models;
using Aevatar.CognitiveMesh.Abstractions;
using Aevatar.CognitiveMesh.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ErrorEvent = Aevatar.AxiomReasoning.EventStreaming.Events.ErrorEvent;
using ResultEvent = Aevatar.AxiomReasoning.EventStreaming.Events.ResultEvent;
using ProgressEvent = Aevatar.AxiomReasoning.EventStreaming.Events.ProgressEvent;
using Aevatar.AxiomReasoning.EventStreaming.Events;
using Aevatar.AxiomReasoning.Graph;
using Aevatar.AxiomReasoning.LlmRecorder;

namespace Aevatar.AxiomReasoning.Services;

// ============================================================
//  AXIOM REASONING SERVICE
//  职责：会话管理 + 启动 CognitiveStrategy(axiom_reasoning.yaml) + SSE
// ============================================================

public sealed class AxiomReasoningService
{
    private readonly ConcurrentDictionary<string, AxiomSession> _sessions = new();
    private readonly CognitiveStrategy _cognitiveStrategy;
    private readonly AxiomReasoningEventBridge _eventBridge;
    private readonly LlmTranscriptRecorder _transcriptRecorder;
    private readonly SupabaseService _supabaseService;
    private readonly IGraphStore _graphStore;
    private readonly IGAgentActorManager _actorManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AxiomReasoningService> _logger;
    private readonly string _outputBasePath;

    public AxiomReasoningService(
        CognitiveStrategy cognitiveStrategy,
        AxiomReasoningEventBridge eventBridge,
        LlmTranscriptRecorder transcriptRecorder,
        SupabaseService supabaseService,
        IGraphStore graphStore,
        IGAgentActorManager actorManager,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _cognitiveStrategy = cognitiveStrategy;
        _eventBridge = eventBridge;
        _transcriptRecorder = transcriptRecorder;
        _supabaseService = supabaseService;
        _graphStore = graphStore;
        _actorManager = actorManager;
        _serviceProvider = serviceProvider;
        _logger = loggerFactory.CreateLogger<AxiomReasoningService>();

        _outputBasePath = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(_outputBasePath);
    }

    // ─────────────────────────────────────────────────────────
    //  会话管理
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetSessions() =>
        _sessions.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                status = s.Status.ToString(),
                s.CreatedAt,
                s.ProgressPercent,
                s.CurrentPhase,
                s.TotalLlmCalls,
                s.TotalTokens,
                workflow = s.Workflow,
                language = s.Language,
                hpaEnabled = s.HpaEnabled,
                hpaBetaModel = s.HpaBetaModel,
                s.K,
                s.MaxRounds,
                maxDepth = s.MaxDepth
            });

    private sealed record CreateSessionRequest
    {
        public string? Axioms { get; init; }
        public string? Goal { get; init; }
        public string? Workflow { get; init; }
        public string? Language { get; init; }
        public int? K { get; init; }
        public int? MaxRounds { get; init; }
        public int? MaxDepth { get; init; }
        public string? ProviderName { get; init; }

        // Long-run budgets (optional)
        public int? MaxDurationMinutes { get; init; }
        public int? MaxLlmCalls { get; init; }
        public long? MaxTokens { get; init; }

        // Workflow behavior
        public bool? ContinueOnFailure { get; init; }

        // HPA (optional; only effective when workflow supports it)
        public bool? HpaEnabled { get; init; }
        public double? HpaAlpha { get; init; }
        public double? HpaSeedPhase { get; init; }
        public string? HpaBetaModel { get; init; }
        public double? HpaBeta0 { get; init; }
        public double? HpaBeta1 { get; init; }
        public int? HpaSeed { get; init; }
        public double? HpaRadialWBase { get; init; }
        public double? HpaRadialWScale { get; init; }
        public double? MinCoherence { get; init; }
        public double? MaxGapNorm { get; init; }
        public double? MaxAssociatorMean { get; init; }
    }

    public async Task<object> CreateSessionAsync(string configJson)
    {
        try
        {
            var req = JsonSerializer.Deserialize<CreateSessionRequest>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("Invalid request");

            var axiomsText = (req.Axioms ?? "").Trim();
            var goal = (req.Goal ?? "").Trim();

            if (string.IsNullOrWhiteSpace(axiomsText))
                return new { success = false, error = "axioms is required" };
            // goal is optional (used as "focus" / direction hint for theorem discovery)

            var session = new AxiomSession
            {
                AxiomsText = axiomsText,
                Goal = goal,
                Workflow = ResolveWorkflow(req.Workflow),
                Language = NormalizeLanguage(req.Language),
                K = req.K is > 0 ? req.K.Value : 3,
                MaxRounds = req.MaxRounds is > 0 ? req.MaxRounds.Value : 10,
                MaxDepth = req.MaxDepth is > 0 ? req.MaxDepth.Value : 10,

                // Budgets (keep prior defaults if not provided)
                MaxDurationMinutes = req.MaxDurationMinutes is > 0 ? Math.Clamp(req.MaxDurationMinutes.Value, 1, 24 * 60) : 30,
                MaxLlmCallsBudget = req.MaxLlmCalls is > 0 ? Math.Clamp(req.MaxLlmCalls.Value, 1, 200_000) : 300,
                MaxTokensBudget = req.MaxTokens is > 0 ? Math.Clamp(req.MaxTokens.Value, 1, 200_000_000) : 800_000,

                ContinueOnFailure = req.ContinueOnFailure ?? false,

                // HPA (opt-in)
                HpaEnabled = req.HpaEnabled ?? false,
                HpaAlpha = NormalizeUnit01(req.HpaAlpha, fallback: 0.6180339887498949),
                HpaSeedPhase = NormalizeUnit01(req.HpaSeedPhase, fallback: 0.0),
                HpaBetaModel = string.IsNullOrWhiteSpace(req.HpaBetaModel) ? "random_prime_phase" : req.HpaBetaModel.Trim(),
                HpaBeta0 = req.HpaBeta0 is > 0 ? req.HpaBeta0.Value : 4.0,
                HpaBeta1 = req.HpaBeta1 is > 0 ? req.HpaBeta1.Value : 2.0,
                HpaSeed = req.HpaSeed ?? 0,
                HpaRadialWBase = req.HpaRadialWBase is >= 0 ? req.HpaRadialWBase.Value : 0.12,
                HpaRadialWScale = req.HpaRadialWScale is >= 0 ? req.HpaRadialWScale.Value : 0.38,
                // HPA gates (exploration-friendly defaults; can be tightened later)
                MinCoherence = req.MinCoherence is >= 0 and <= 1 ? req.MinCoherence.Value : 0.55,
                MaxGapNorm = req.MaxGapNorm is > 0 ? req.MaxGapNorm.Value : 0.65,
                MaxAssociatorMean = req.MaxAssociatorMean is > 0 ? req.MaxAssociatorMean.Value : 1.5
            };

            // Bootstrap state for AG-UI status snapshot (we don't rely on replay).
            session.CurrentPhase = "CREATED";
            session.ProgressPercent = 0;

            _sessions[session.Id] = session;
            _logger.LogInformation("Created axiom session: {Id}", session.Id);

            // 立即发一个初始事件，方便前端接入
            session.EventHub.Publish(new ProgressEvent
            {
                SessionId = session.Id,
                Phase = "CREATED",
                Message = "Session created",
                ProgressPercent = 0
            });

            // ProviderName 作为“运行参数”不放进 session（避免泄漏/混淆），运行时从请求读取或走默认
            await Task.CompletedTask;

            return new
            {
                success = true,
                sessionId = session.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return new { success = false, error = ex.Message };
        }
    }

    private string ResolveWorkflow(string? requested)
    {
        var wf = string.IsNullOrWhiteSpace(requested) ? "hypothesis_promotion_loop" : requested.Trim();

        try
        {
            var available = _cognitiveStrategy.GetAvailableWorkflows();
            if (available.Any(x => string.Equals(x, wf, StringComparison.OrdinalIgnoreCase)))
                return wf;

            _logger.LogWarning("Unknown workflow '{Workflow}', falling back to hypothesis_promotion_loop. Available=[{List}]",
                wf, string.Join(", ", available));
        }
        catch (Exception ex)
        {
            // best-effort: if registry cannot be read, still allow default workflow
            _logger.LogWarning(ex, "Failed to list workflows; using default workflow");
        }

        return "hypothesis_promotion_loop";
    }

    private static string NormalizeLanguage(string? requested)
    {
        var raw = (requested ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "English";

        // Common aliases
        var lower = raw.ToLowerInvariant();
        if (lower is "zh" or "zh-cn" or "zh-hans" or "cn" or "chinese" or "中文" or "汉语")
            return "Chinese";
        if (lower is "en" or "en-us" or "en-gb" or "english" or "英文")
            return "English";

        // If user already passes a natural language label (e.g. "Japanese"), keep it.
        return raw;
    }

    private static double NormalizeUnit01(double? value, double fallback)
    {
        if (!value.HasValue) return fallback;
        var x = value.Value;
        if (double.IsNaN(x) || double.IsInfinity(x)) return fallback;
        x = x - Math.Floor(x);
        if (x < 0) x += 1.0;
        return x;
    }

    // ─────────────────────────────────────────────────────────
    //  执行
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status == AxiomSessionStatus.Running)
            return new { success = false, error = "Already running" };

        session.Status = AxiomSessionStatus.Running;
        session.OutputDir = Path.Combine(_outputBasePath, session.Id);
        Directory.CreateDirectory(session.OutputDir);

        // Initialize local transcript outputs (best-effort)
        _transcriptRecorder.OnSessionStarted(session);

        _ = ExecuteAsync(session, session.CancellationTokenSource.Token);
        await Task.CompletedTask;

        return new { success = true, sessionId };
    }

    public object Stop(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status != AxiomSessionStatus.Running)
            return new { success = false, error = "Not running" };

        try
        {
            session.CancellationTokenSource.Cancel();
            session.Status = AxiomSessionStatus.Cancelled;
            session.Error = "Stopped by user";

            session.EventHub.Publish(new ErrorEvent
            {
                SessionId = session.Id,
                Message = "Stopped by user"
            });

            session.EventHub.Complete();
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop session {Id}", sessionId);
            return new { success = false, error = ex.Message };
        }
    }

    private async Task ExecuteAsync(AxiomSession session, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var task = BuildTask(session);
            var options = BuildReasoningOptions(session);
            var progress = new Progress<ReasoningProgress>(p =>
            {
                // IMPORTANT:
                // - Progress<T> 回调抛异常会直接杀进程
                // - event bridge / transcript recorder 都必须是 best-effort
                try
                {
                    _eventBridge.HandleProgress(session, p);
                    _transcriptRecorder.HandleProgress(session, p);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Id}] Progress pipeline crashed (ignored)", session.Id);
                }
            });

            _logger.LogInformation("[{Id}] Starting workflow={Workflow}, K={K}, N={N}, max_depth={Depth}",
                session.Id, options.CognitiveWorkflow, options.CognitiveConsensusK, options.CognitiveWorkerCount, options.CognitiveMaxDepth);

            var result = await _cognitiveStrategy.ExecuteAsync(task, options, progress, ct);

            sw.Stop();
            session.Duration = sw.Elapsed;
            session.Result = result;
            session.TotalLlmCalls = result.TotalLlmCalls;
            session.TotalTokens = result.TotalTokens;

            session.Status = result.Success ? AxiomSessionStatus.Completed : AxiomSessionStatus.Failed;
            session.Error = result.Error;

            // 保存 artifacts（best-effort）
            string? stateJson = null;
            string? theoremsJson = null;
            try
            {
                SaveFile(session, "artifacts", "result.txt", result.Content ?? "");

                // axiom_theorem_loop.yaml outputs `state` (object) + `theorems` (list)
                stateJson = ExtractField(result.Content, "state") ?? "{}";
                theoremsJson = ExtractField(result.Content, "theorems") ?? "[]";
                SaveFile(session, "artifacts", "state.json", stateJson);
                SaveFile(session, "artifacts", "theorems.json", theoremsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to save artifacts (ignored)", session.Id);
            }

            // Dependency Graph (DAG) persistence + SSE snapshot (best-effort)
            // WHY:
            // - HPL/HPA workflows don't necessarily emit a dedicated "update_state" llm_call step.
            // - We still want the UI dependency graph to be non-empty at least at completion.
            try
            {
                if (TryBuildGraphFromStateJson(stateJson, out var graph))
                {
                    await _graphStore.UpsertFromGraphEventAsync(session.Id, graph, CancellationToken.None);
                    session.EventHub.Publish(graph with { SessionId = session.Id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to update dependency graph (ignored)", session.Id);
            }

            // Supabase persistence (best-effort)
            try
            {
                if (_supabaseService.IsEnabled)
                {
                    await _supabaseService.SaveResultAsync(
                        sessionId: session.Id,
                        axiomsText: session.AxiomsText,
                        goal: session.Goal,
                        status: session.Status.ToString(),
                        stateJson: stateJson,
                        theoremsJson: theoremsJson,
                        content: result.Content,
                        error: result.Error,
                        llmCalls: result.TotalLlmCalls,
                        totalTokens: result.TotalTokens,
                        durationSeconds: session.Duration.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to persist result to Supabase (ignored)", session.Id);
            }

            session.EventHub.Publish(new ResultEvent
            {
                SessionId = session.Id,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalLlmCalls = result.TotalLlmCalls,
                TotalTokens = result.TotalTokens
            });

            if (!result.Success)
            {
                _logger.LogError("[{Id}] Workflow failed: {Error}", session.Id, result.Error ?? "(unknown)");
                session.EventHub.Publish(new ErrorEvent
                {
                    SessionId = session.Id,
                    Message = result.Error ?? "Execution failed"
                });
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            session.Duration = sw.Elapsed;
            session.Status = AxiomSessionStatus.Failed;
            session.Error = ex.Message;

            _logger.LogError(ex, "[{Id}] Execution failed", session.Id);
            session.EventHub.Publish(new ErrorEvent
            {
                SessionId = session.Id,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            // Always flush local transcript before ending the session.
            _transcriptRecorder.OnSessionCompleted(session);
            session.EventHub.Complete();
        }
    }

    private static string BuildTask(AxiomSession session)
    {
        // NOTE:
        // - CognitiveStrategy 只会注入 `task`/`context` 变量给 workflow。
        // - theorem-loop workflow 会从 raw_task 中提取 axioms + optional focus。
        var focus = string.IsNullOrWhiteSpace(session.Goal) ? "" : session.Goal.Trim();

        return $"""
               HYPOTHESIS PROMOTION LOOP (HPL)

               AXIOMS (one per line, authoritative):
               {session.AxiomsText}

               Focus (optional):
               {focus}

               ContinueOnFailure:
               {session.ContinueOnFailure}
               """;
    }

    private ReasoningOptions BuildReasoningOptions(AxiomSession session)
    {
        var k = Math.Clamp(session.K, 1, 9);
        var n = Math.Clamp(2 * k - 1, 1, 15);

        var ctx = new Dictionary<string, string>
        {
            // Deterministic agent ids (Coordinator/Workers) for reconnect + persistence.
            // NOTE:
            // - CognitiveStrategy will use this to derive stable Guid keys.
            // - With a persistent StateStore, we can re-create actors after restart and still hydrate UI.
            ["session_id"] = session.Id,
            ["enable_chat_history"] = "true",

            // Propagate workflow behavior flags to CognitiveStrategy initial variables
            ["continue_on_failure"] = session.ContinueOnFailure ? "true" : "false",
            ["language"] = session.Language
        };

        // HPA knobs: only propagate when enabled
        if (session.HpaEnabled)
        {
            ctx["hpa_alpha"] = session.HpaAlpha.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_seed_phase"] = session.HpaSeedPhase.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_beta_model"] = session.HpaBetaModel;
            ctx["hpa_beta0"] = session.HpaBeta0.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_beta1"] = session.HpaBeta1.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_seed"] = session.HpaSeed.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_radial_w_base"] = session.HpaRadialWBase.ToString(CultureInfo.InvariantCulture);
            ctx["hpa_radial_w_scale"] = session.HpaRadialWScale.ToString(CultureInfo.InvariantCulture);
            ctx["min_coherence"] = session.MinCoherence.ToString(CultureInfo.InvariantCulture);
            ctx["max_gap_norm"] = session.MaxGapNorm.ToString(CultureInfo.InvariantCulture);
            ctx["max_associator_mean"] = session.MaxAssociatorMean.ToString(CultureInfo.InvariantCulture);
        }

        return new ReasoningOptions
        {
            ProviderName = Aevatar.Agents.Abstractions.AevatarAgentsConstants.DefaultProviderName,
            MaxLlmCalls = session.MaxLlmCallsBudget,
            MaxTokens = session.MaxTokensBudget,
            MaxDuration = TimeSpan.FromMinutes(Math.Clamp(session.MaxDurationMinutes, 1, 24 * 60)),
            Context = ctx,

            // Theorem discovery loop: coordinator proposes -> workers prove -> vote judge -> iterate
            CognitiveWorkflow = session.Workflow,
            CognitiveWorkerCount = n,
            CognitiveConsensusK = k,
            CognitiveMaxRounds = Math.Clamp(session.MaxRounds, 1, 50),
            CognitiveMaxDepth = Math.Clamp(session.MaxDepth, 1, 200),
            CognitiveSemanticSimilarity = 0.85f,
            CognitiveTimeoutMinutes = Math.Clamp(session.MaxDurationMinutes, 1, 24 * 60)
        };
    }

    // ─────────────────────────────────────────────────────────
    //  查询
    // ─────────────────────────────────────────────────────────

    public object GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { status = "not_found" };

        return new
        {
            status = session.Status.ToString().ToLowerInvariant(),
            sessionId = session.Id,
            session.ProgressPercent,
            phase = session.CurrentPhase,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public object GetResult(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        return new
        {
            success = session.Status == AxiomSessionStatus.Completed,
            content = session.Result?.Content,
            error = session.Error,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public IEnumerable<object> GetArtifacts(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        if (!session.Files.TryGetValue("artifacts", out var files))
            return [];

        return files.Keys.OrderBy(k => k).Select(name => new { category = "artifacts", name });
    }

    public bool TryGetFileContent(string sessionId, string category, string fileName, out string content)
    {
        content = "";
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        if (!session.Files.TryGetValue(category, out var files)) return false;
        if (!files.TryGetValue(fileName, out var c)) return false;
        content = c;
        return true;
    }

    public bool TryReadOutputFileBytes(string sessionId, string category, string fileName, out byte[] bytes)
    {
        bytes = [];
        if (!_sessions.ContainsKey(sessionId)) return false;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(fileName)) return false;

        try
        {
            var path = Path.Combine(_outputBasePath, sessionId, category, fileName);
            if (!File.Exists(path)) return false;
            bytes = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to read output file: {Category}/{File}", sessionId, category, fileName);
            return false;
        }
    }

    public async IAsyncEnumerable<AxiomEvent> GetEventStreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            yield break;

        await foreach (var evt in session.EventHub.SubscribeAsync(replay: true, ct: ct))
        {
            yield return evt;
        }
    }

    public async IAsyncEnumerable<AgUiEvent> GetAgUiEventStreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            yield break;

        // AG-UI reconnect semantics:
        // - Prefer deterministic snapshots over replaying a burst of token/progress events.
        var bootstrap = await AxiomAgUiBootstrap.BuildMessagesSnapshotAsync(
            session,
            _actorManager,
            maxAssistantMessages: 60,
            ct: ct);

        var initialGraph = await AxiomAgUiBootstrap.TryBuildGraphSnapshotAsync(
            _graphStore,
            sessionId,
            ct);

        await foreach (var evt in AxiomAgUiEventStream.BuildAsync(
                           session,
                           session.EventHub.SubscribeAsync(replay: false, ct: ct),
                           bootstrap.Messages,
                           initialGraph,
                           bootstrap.ExtraEvents,
                           ct))
        {
            yield return evt;
        }
    }

    private void SaveFile(AxiomSession session, string category, string fileName, string content)
    {
        session.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (string.IsNullOrEmpty(session.OutputDir)) return;

        try
        {
            var dir = Path.Combine(session.OutputDir, category);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to write file {File}", session.Id, fileName);
        }
    }

    // ============================================================
    //  解析 output 字段（best-effort）
    //
    //  CognitiveStrategy 会把 workflow 输出序列化为“字符串”，其格式可能是：
    //  - JSON string（如果 output 是字典，可能被 SerializeOutput 转为 JSON）
    //  - plain text
    //
    //  这里做最小可用：
    //  1) 若 content 是 JSON object，尝试提取字段
    //  2) 否则返回 null
    // ============================================================
    private static string? ExtractField(string? content, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(fieldName, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                _ => value.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildGraphFromStateJson(string? stateJson, out GraphEvent graph)
    {
        graph = new GraphEvent();
        if (string.IsNullOrWhiteSpace(stateJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = doc.RootElement;

            var axioms = new List<string>();
            if (root.TryGetProperty("axioms", out var ax) && ax.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in ax.EnumerateArray())
                {
                    if (a.ValueKind == JsonValueKind.String) axioms.Add(a.GetString() ?? "");
                }
            }

            var assumptions = new List<AssumptionNode>();
            if (root.TryGetProperty("assumptions", out var asm) && asm.ValueKind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var a in asm.EnumerateArray())
                {
                    idx++;
                    if (a.ValueKind == JsonValueKind.Object)
                    {
                        var id = a.TryGetProperty("id", out var aid) && aid.ValueKind == JsonValueKind.String ? aid.GetString() ?? "" : "";
                        var stmt = a.TryGetProperty("statement", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
                        var mot = a.TryGetProperty("motivation", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(id)) id = $"S{idx}";
                        assumptions.Add(new AssumptionNode { Id = id, Statement = stmt, Motivation = mot });
                    }
                    else if (a.ValueKind == JsonValueKind.String)
                    {
                        assumptions.Add(new AssumptionNode { Id = $"S{idx}", Statement = a.GetString() ?? "", Motivation = "" });
                    }
                }
            }

            var theorems = new List<TheoremNode>();
            if (root.TryGetProperty("theorems", out var th) && th.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in th.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var id = t.TryGetProperty("id", out var tid) && tid.ValueKind == JsonValueKind.String ? tid.GetString() ?? "" : "";
                    var stmt = t.TryGetProperty("statement", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
                    var proof = t.TryGetProperty("proof", out var pf) && pf.ValueKind == JsonValueKind.String ? pf.GetString() ?? "" : "";

                    var deps = new List<string>();
                    if (t.TryGetProperty("depends_on", out var dp) && dp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dp.EnumerateArray())
                        {
                            if (d.ValueKind == JsonValueKind.String) deps.Add(d.GetString() ?? "");
                        }
                    }
                    else if (t.TryGetProperty("dependsOn", out var dp2) && dp2.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in dp2.EnumerateArray())
                        {
                            if (d.ValueKind == JsonValueKind.String) deps.Add(d.GetString() ?? "");
                        }
                    }

                    theorems.Add(new TheoremNode { Id = id, Statement = stmt, Proof = proof, DependsOn = deps });
                }
            }

            var iteration = root.TryGetProperty("iteration", out var it) && it.ValueKind == JsonValueKind.Number
                ? it.GetInt32()
                : theorems.Count;

            graph = new GraphEvent
            {
                Iteration = iteration,
                Axioms = axioms,
                Assumptions = assumptions,
                Theorems = theorems
            };

            return axioms.Count > 0 || assumptions.Count > 0 || theorems.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}