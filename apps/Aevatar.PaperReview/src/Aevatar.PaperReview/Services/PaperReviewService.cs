using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.Cognitive.Content;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Core.Strategies;
using Aevatar.PaperReview.Models;
using ErrorEvent = Aevatar.PaperReview.Models.ErrorEvent;
using ResultEvent = Aevatar.PaperReview.Models.ResultEvent;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  PAPER REVIEW SERVICE
//  职责：会话管理与评审流程编排
// ============================================================

/// <summary>
/// 论文评审服务 - 基于 Cognitive Mesh DSL 的多专家共识系统。
/// </summary>
public sealed class PaperReviewService
{
    private readonly ConcurrentDictionary<string, ReviewSession> _sessions = new();
    private readonly ProjectStore _projectStore;
    private readonly CognitiveStrategy _cognitiveStrategy;
    private readonly PaperUploadService _uploadService;
    private readonly ReviewPromptProvider _promptProvider;
    private readonly ReviewEventBridge _eventBridge;
    private readonly SupabaseService _supabase;
    private readonly ILogger<PaperReviewService> _logger;
    private readonly string _outputBasePath;

    public PaperReviewService(
        SupabaseService supabase,
        PaperUploadService uploadService,
        ReviewPromptProvider promptProvider,
        ReviewEventBridge eventBridge,
        CognitiveStrategy cognitiveStrategy,
        ILoggerFactory loggerFactory)
    {
        _supabase = supabase;
        _uploadService = uploadService;
        _promptProvider = promptProvider;
        _eventBridge = eventBridge;
        _cognitiveStrategy = cognitiveStrategy;
        _logger = loggerFactory.CreateLogger<PaperReviewService>();
        _projectStore = new ProjectStore(loggerFactory.CreateLogger<ProjectStore>());
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
                s.Title,
                s.Authors,
                type = s.Type.ToString(),
                s.VenueType,
                status = s.Status.ToString(),
                s.CreatedAt,
                s.ProgressPercent,
                s.TotalLlmCalls,
                s.TotalTokens
            });

    public async Task<object> CreateSessionAsync(string configJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateSessionRequest>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("Invalid request");

            var session = new ReviewSession
            {
                Title = request.Title,
                Authors = request.Authors,
                Type = Enum.TryParse<ReviewType>(request.ReviewType, true, out var rt) ? rt : ReviewType.Standard,
                VenueType = request.VenueType ?? "AI Conference",
                UploadId = request.UploadId
            };

            // 加载论文内容
            await LoadPaperContentAsync(session, request);

            // 默认值
            if (string.IsNullOrWhiteSpace(session.Title)) session.Title = "Untitled";
            if (string.IsNullOrWhiteSpace(session.Authors)) session.Authors = "Unknown";

            _sessions[session.Id] = session;
            _logger.LogInformation("Created session: {Id} - {Title}", session.Id, session.Title);

            return new
            {
                success = true,
                sessionId = session.Id,
                session.Title,
                type = session.Type.ToString(),
                hasPaper = !string.IsNullOrEmpty(session.PaperContent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return new { success = false, error = ex.Message };
        }
    }

    public async Task<object> UploadPaperAsync(IFormFileCollection files, CancellationToken ct)
    {
        var result = await _uploadService.UploadAsync(files, ct);
        return result.Success
            ? new { success = true, uploadId = result.UploadId, fileName = result.FileName, size = result.Size }
            : new { success = false, error = result.Error };
    }

    // ─────────────────────────────────────────────────────────
    //  评审执行
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartReviewAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status == ReviewStatus.Reviewing)
            return new { success = false, error = "Already reviewing" };

        if (string.IsNullOrEmpty(session.PaperContent))
            return new { success = false, error = "No paper content" };

        session.Status = ReviewStatus.Reviewing;
        session.OutputDir = Path.Combine(_outputBasePath, session.Id);
        Directory.CreateDirectory(session.OutputDir);

        _logger.LogInformation("Starting review: {Id} - {Title}", sessionId, session.Title);

        _ = ExecuteReviewAsync(session, session.CancellationTokenSource.Token);

        return new { success = true, sessionId };
    }

    public object StopReview(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status != ReviewStatus.Reviewing)
            return new { success = false, error = "Not reviewing" };

        try
        {
            session.CancellationTokenSource.Cancel();
            session.Status = ReviewStatus.Cancelled;
            session.Error = "Stopped by user";

            session.EventChannel.Writer.TryWrite(new ErrorEvent
            {
                SessionId = sessionId,
                Message = "Review stopped by user"
            });
            session.EventChannel.Writer.TryComplete();

            _logger.LogInformation("Stopped review: {Id}", sessionId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop review: {Id}", sessionId);
            return new { success = false, error = ex.Message };
        }
    }

    // ─────────────────────────────────────────────────────────
    //  状态查询
    // ─────────────────────────────────────────────────────────

    public object? GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { status = "not_found" };

        return new
        {
            status = session.Status.ToString().ToLower(),
            sessionId = session.Id,
            session.Title,
            type = session.Type.ToString(),
            session.ProgressPercent,
            phase = session.CurrentPhase,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public object? GetResult(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        var content = session.Files.TryGetValue("reports", out var reports)
            ? reports.GetValueOrDefault("review_report.md")
            : null;

        return new
        {
            success = session.Status == ReviewStatus.Completed,
            content,
            error = session.Error,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public IEnumerable<object> GetHistory(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        return session.Timeline.Select(e => new
        {
            phase = e.Phase,
            message = e.Message,
            timestamp = e.Timestamp
        });
    }

    public async IAsyncEnumerable<ReviewEvent> GetEventStreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            yield break;

        await foreach (var evt in session.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
    
    // ─────────────────────────────────────────────────────────
    //  Deliverables (交付物 / 下载)
    // ─────────────────────────────────────────────────────────
    
    public bool TryGetFileContent(string sessionId, string category, string fileName, out string content)
    {
        content = "";
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        if (!session.Files.TryGetValue(category, out var files)) return false;
        return files.TryGetValue(fileName, out content!);
    }
    
    public object GetArtifacts(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };
        
        var list = new List<object>();
        foreach (var (category, files) in session.Files)
        {
            foreach (var (name, value) in files)
            {
                list.Add(new
                {
                    category,
                    name,
                    size = value?.Length ?? 0
                });
            }
        }
        
        return new
        {
            success = true,
            sessionId,
            artifacts = list
        };
    }

    // ─────────────────────────────────────────────────────────
    //  私有方法
    // ─────────────────────────────────────────────────────────

    private async Task LoadPaperContentAsync(ReviewSession session, CreateSessionRequest request)
    {
        if (!string.IsNullOrEmpty(request.PaperContent))
        {
            session.PaperContent = request.PaperContent;
        }
        else if (!string.IsNullOrEmpty(request.CopyFromSessionId)
                 && _sessions.TryGetValue(request.CopyFromSessionId, out var oldSession))
        {
            session.PaperContent = oldSession.PaperContent;
            session.UploadId = oldSession.UploadId;
            _logger.LogInformation("Copied content from session {Old} to {New}",
                request.CopyFromSessionId, session.Id);
        }
        else if (!string.IsNullOrEmpty(request.UploadId))
        {
            var (content, fileName) = await _uploadService.LoadContentAsync(request.UploadId);
            session.PaperContent = content;
            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = string.IsNullOrWhiteSpace(fileName) ? "Untitled" : fileName;
        }
    }

    private async Task ExecuteReviewAsync(ReviewSession session, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        await _supabase.SaveReviewAsync(
            session.Id, session.Title, session.Authors,
            session.Type.ToString(), session.VenueType,
            "Reviewing", null, null, 0, 0, 0);

        try
        {
            var reviewTask = BuildReviewTask(session);
            var options = BuildReasoningOptions(session);
            var progress = new Progress<ReasoningProgress>(p => _eventBridge.HandleProgress(session, p));

            _logger.LogInformation("[{Id}] Starting workflow={Workflow}, K={K}, N={N}",
                session.Id, options.CognitiveWorkflow, options.CognitiveConsensusK, options.CognitiveWorkerCount);

            var result = await _cognitiveStrategy.ExecuteAsync(reviewTask, options, progress, ct);

            stopwatch.Stop();
            session.Status = result.Success ? ReviewStatus.Completed : ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.TotalLlmCalls = result.TotalLlmCalls;
            session.TotalTokens = result.PromptTokens + result.CompletionTokens;

            _logger.LogInformation("[{Id}] Complete. Success={Success}, Duration={Dur}s",
                session.Id, result.Success, stopwatch.Elapsed.TotalSeconds);

            string? reportUrl = null;
            
            // ============================================================
            //  Deliverables (交付物)
            //  - 每次评审输出两个文件：
            //    1) reports/review_report.md     : compose 后的最终文档（即使失败也保留元信息）
            //    2) details/review_details.json  : atomic points 的 task + consensus（best-effort）
            // ============================================================
            var report = FormatReport(session, result);
            SaveFile(session, "reports", "review_report.md", report);
            
            // 云端上传（可选）
            if (!string.IsNullOrWhiteSpace(report))
            {
                reportUrl = await _supabase.UploadReportAsync(session.Id, report);
            }
            
            // details：不应影响主流程，失败也只打警告
            try
            {
                var detailsJson = _eventBridge.BuildReviewDetailsJson(session);
                SaveFile(session, "details", "review_details.json", detailsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to build review_details.json (ignored)", session.Id);
            }

            await _supabase.UpdateReviewAsync(
                session.Id, session.Status.ToString(), result.Content, result.Error,
                result.TotalLlmCalls, result.TotalTokens, session.Duration.TotalSeconds, reportUrl);

            SendEvent(session, new ResultEvent
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
                SendEvent(session, new ErrorEvent
                {
                    SessionId = session.Id,
                    Message = result.Error ?? "Review failed"
                });
            }
            
            // 清理桥接层 session 缓存（避免长期运行内存增长）
            try
            {
                _eventBridge.CleanupSession(session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[{Id}] CleanupSession failed (ignored)", session.Id);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            session.Status = ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.Error = ex.Message;

            _logger.LogError(ex, "[{Id}] Review failed", session.Id);

            await _supabase.UpdateReviewAsync(
                session.Id, "Failed", null, ex.Message,
                session.TotalLlmCalls, session.TotalTokens, session.Duration.TotalSeconds);

            SendEvent(session, new ErrorEvent
            {
                SessionId = session.Id,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            session.EventChannel.Writer.TryComplete();
        }
    }

    private string BuildReviewTask(ReviewSession session)
    {
        var (dimCount, scrutinyLevel, focusAreas) = session.Type switch
        {
            ReviewType.Quick => (2, "high-level", "core contribution and major flaws"),
            ReviewType.Standard => (4, "standard", "technical soundness, novelty, experiments, presentation"),
            ReviewType.Detailed => (5, "detailed", "methodology, innovation, evaluation, clarity, related work"),
            ReviewType.Rigorous => (6, "rigorous", "all technical aspects with deep scrutiny"),
            ReviewType.Critical => (7, "exhaustive", "every aspect with maximum rigor and skepticism"),
            _ => (4, "standard", "key aspects")
        };

        return _promptProvider.RenderReviewTaskAsText(new ReviewTaskContext
        {
            VenueType = session.VenueType ?? "AI Conference",
            ScrutinyLevel = scrutinyLevel,
            DimensionCount = dimCount,
            FocusAreas = focusAreas,
            Title = session.Title ?? "Untitled",
            Authors = session.Authors ?? "Unknown",
            PaperContent = session.PaperContent ?? ""
        });
    }

    private ReasoningOptions BuildReasoningOptions(ReviewSession session)
    {
        var (k, n, desc) = MakerParameters.GetParams(session.Type);

        var project = _projectStore.GetProject("paper-review");
        var baseOpts = project?.Options ?? new ReasoningOptions
        {
            ProviderName = "deepseek",
            MakerReliability = MakerReliability.High,
            MaxLlmCalls = 400,
            MaxTokens = 800_000
        };

        var reliability = session.Type switch
        {
            ReviewType.Quick => MakerReliability.Low,
            ReviewType.Standard => MakerReliability.Medium,
            ReviewType.Detailed => MakerReliability.High,
            ReviewType.Rigorous => MakerReliability.Critical,
            ReviewType.Critical => MakerReliability.Critical,
            _ => MakerReliability.Medium
        };

        _logger.LogInformation("[{Id}] MAKER: {Type} → K={K}, N={N} ({Desc})",
            session.Id, session.Type, k, n, desc);

        SendEvent(session, new StageLogEvent
        {
            SessionId = session.Id,
            Stage = "Configuration",
            Status = "completed",
            Summary = $"MAKER initialized: K={k}, N={n} workers ({desc})",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            Stats = new StageStats { WorkerCount = n }
        });

        var estimatedDimensions = session.Type switch
        {
            ReviewType.Quick => 3,
            ReviewType.Standard => 5,
            ReviewType.Detailed => 6,
            ReviewType.Rigorous => 7,
            ReviewType.Critical => 8,
            _ => 5
        };

        var llmCallBudget = n * (1 + estimatedDimensions * 4) + 20;
        var tokenBudget = 200_000 * n;

        var ctx = new Dictionary<string, string>(baseOpts.Context ?? new Dictionary<string, string>())
        {
            ["paper.content"] = session.PaperContent ?? "",
            ["paper.title"] = session.Title ?? "Untitled",
            ["paper.venue"] = session.VenueType ?? "AI Conference",
            ["paper.reviewType"] = session.Type.ToString()
        };

        return baseOpts with
        {
            MakerReliability = reliability,
            MakerConsensusK = k,
            MakerExecutionMode = MakerExecutionMode.Academic,
            MaxLlmCalls = Math.Max(baseOpts.MaxLlmCalls, llmCallBudget),
            MaxTokens = Math.Max(baseOpts.MaxTokens, tokenBudget),
            // 论文评审属于“长任务”：真实场景下经常超过 30min
            // - 至少 1h，避免中途超时导致前面产出作废
            MaxDuration = TimeSpan.FromHours(1),
            Context = ctx,
            CognitiveWorkflow = "maker",
            CognitiveWorkerCount = n,
            CognitiveConsensusK = k,
            CognitiveMaxRounds = 10,
            CognitiveMaxDepth = 8,
            CognitiveSemanticSimilarity = 0.85f,
            CognitiveTimeoutMinutes = 60
        };
    }

    private string FormatReport(ReviewSession session, ReasoningResult result)
    {
        return _promptProvider.RenderReport(new ReviewReportContext
        {
            Title = session.Title ?? "Untitled",
            Authors = session.Authors ?? "Unknown",
            VenueType = session.VenueType ?? "AI Conference",
            ReviewType = session.Type.ToString(),
            SessionId = session.Id,
            Success = result.Success,
            DurationSeconds = session.Duration.TotalSeconds,
            LlmCalls = result.TotalLlmCalls,
            TotalTokens = result.TotalTokens,
            Content = result.Content ?? ""
        });
    }

    private void SendEvent(ReviewSession session, ReviewEvent evt) =>
        session.EventChannel.Writer.TryWrite(evt);

    private void SaveFile(ReviewSession session, string category, string fileName, string content)
    {
        session.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (!string.IsNullOrEmpty(session.OutputDir))
        {
            try
            {
                var dir = Path.Combine(session.OutputDir, category);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save file: {Category}/{FileName}", category, fileName);
            }
        }
    }
}
