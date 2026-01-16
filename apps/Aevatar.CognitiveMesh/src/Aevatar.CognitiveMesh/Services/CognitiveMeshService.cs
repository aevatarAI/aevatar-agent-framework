using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Content;
using Aevatar.Agents.Cognitive.Core.Tasks;
using Aevatar.CognitiveMesh.Models;

namespace Aevatar.CognitiveMesh.Services;

// ============================================================
//  COGNITIVE MESH SERVICE
//  认知网格核心服务 - 项目管理与执行调度
// ============================================================

/// <summary>
/// 认知网格核心服务。
/// 统一管理 MAKER 和 UoT 等策略的项目执行。
/// </summary>
public sealed class CognitiveMeshService
{
    private readonly StrategyRegistry _registry;
    private readonly IContentLoader _contentLoader;
    private readonly ProjectStore _projectStore;
    private readonly ILogger<CognitiveMeshService> _logger;
    private readonly ConcurrentDictionary<string, MeshRun> _runs = new();
    private readonly ConcurrentDictionary<string, List<RunHistoryEntry>> _runHistory = new();
    private readonly string _uploadsBasePath;
    private readonly string _outputBasePath;

    // ─────────────────────────────────────────────────────────
    //  项目访问器 (从 YAML 加载)
    // ─────────────────────────────────────────────────────────
    private IEnumerable<MeshProject> AllProjects => _projectStore.GetAllProjects();

    public CognitiveMeshService(
        StrategyRegistry registry,
        IContentLoader contentLoader,
        ProjectStore projectStore,
        ILogger<CognitiveMeshService> logger)
    {
        _registry = registry;
        _contentLoader = contentLoader;
        _projectStore = projectStore;
        _logger = logger;
        _uploadsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        _outputBasePath = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(_uploadsBasePath);
        Directory.CreateDirectory(_outputBasePath);
    }

    // ─────────────────────────────────────────────────────────
    //  项目管理
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetProjects() =>
        AllProjects.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.Icon,
            strategy = p.Strategy.ToString(),
            strategyDisplayName = p.Strategy.GetDisplayName()
        });

    public object? GetProjectConfig(string projectId)
    {
        var project = AllProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return null;

        return new
        {
            id = project.Id,
            name = project.Name,
            description = project.Description,
            task = project.Task,
            strategy = project.Strategy.ToString(),
            strategyDisplayName = project.Strategy.GetDisplayName(),
            options = new
            {
                // MAKER
                reliability = project.Options.MakerReliability.ToString(),
                maxLlmCalls = project.Options.MaxLlmCalls,
                maxTokens = project.Options.MaxTokens,
                // UoT
                domainHint = project.Options.UotDomainHint,
                maxAnalogies = project.Options.UotMaxAnalogies,
                maxCandidates = project.Options.UotMaxCandidates,
                // E-UoT
                maxOutsideThoughts = project.Options.EUotMaxOutsideThoughts,
                explorationDirections = project.Options.EUotExplorationDirections,
                // T-UoT
                maxRuleSets = project.Options.TUotMaxRuleSets,
                minRadicality = project.Options.TUotMinRadicality,
                // Cognitive DSL
                cognitiveWorkflow = project.Options.CognitiveWorkflow,
                cognitiveWorkerCount = project.Options.CognitiveWorkerCount,
                cognitiveConsensusK = project.Options.CognitiveConsensusK,
                cognitiveMaxRounds = project.Options.CognitiveMaxRounds,
                cognitiveMaxDepth = project.Options.CognitiveMaxDepth,
                cognitiveSemanticSimilarity = project.Options.CognitiveSemanticSimilarity,
                cognitiveTimeoutMinutes = project.Options.CognitiveTimeoutMinutes
            },
            content = project.ContentSource != null ? new
            {
                filePath = project.ContentSource.FilePath,
                directoryPath = project.ContentSource.DirectoryPath,
                filePaths = project.ContentSource.FilePaths,
                uploadId = project.ContentSource.UploadId,
                directContent = project.ContentSource.DirectContent != null ? "(inline text)" : null,
                description = project.ContentSource.ContentDescription
            } : null,
            taskTemplate = project.TaskDefinition?.Template.ToString()
        };
    }

    public object CreateProject(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ProjectConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("无法解析项目配置");

            // 解析任务定义
            TaskDefinition? taskDefinition = config.TaskConfig?.ToTaskDefinition();

            // 确定任务文本
            var task = config.Task;
            if (string.IsNullOrEmpty(task) && taskDefinition != null)
            {
                task = $"Execute {taskDefinition.Template.GetDisplayName()} task";
            }
            if (string.IsNullOrEmpty(task))
            {
                throw new ArgumentException("Task is required (either 'task' or 'taskConfig' must be provided)");
            }

            // 确定图标
            var icon = config.Icon ?? GetIconForTemplate(taskDefinition?.Template);

            // 构建 YAML 项目定义并保存
            var yamlDef = new YamlProjectDefinition
            {
                Name = config.Name ?? "Custom Project",
                Description = config.Description ?? "",
                Icon = icon,
                Strategy = config.Strategy ?? "Maker",
                Task = task,
                TaskTemplate = taskDefinition?.Template.ToString(),
                CustomInstruction = config.TaskConfig?.CustomInstruction,
                CreatedAt = DateTime.UtcNow,
                Options = new YamlProjectOptions
                {
                    ProviderName = config.ProviderName ?? AevatarAgentsConstants.DefaultProviderName,
                    MaxLlmCalls = config.MaxLlmCalls,
                    MaxTokens = config.MaxTokens,
                    Reliability = config.Reliability,
                    DomainHint = config.DomainHint,
                    MaxAnalogies = config.MaxAnalogies,
                    MaxCandidates = config.MaxCandidates,
                    MaxOutsideThoughts = config.MaxOutsideThoughts,
                    ExplorationDirections = config.ExplorationDirections,
                    MaxRuleSets = config.MaxRuleSets,
                    MinRadicality = config.MinRadicality
                }
            };

            // 保存内容来源配置
            if (config.Content != null)
            {
                yamlDef.Content = new YamlContentSource
                {
                    FilePath = config.Content.FilePath,
                    DirectoryPath = config.Content.DirectoryPath,
                    FilePaths = config.Content.FilePaths,
                    UploadId = config.Content.UploadId,
                    ContentDescription = config.Content.ContentDescription,
                    Extensions = config.Content.Extensions,
                    Recursive = config.Content.Recursive
                };
            }

            // 保存到 YAML
            var projectId = _projectStore.AddProject(yamlDef);

            _logger.LogInformation("Created project: {ProjectId} - {Name} with strategy {Strategy}",
                projectId, yamlDef.Name, yamlDef.Strategy);

            return new
            {
                success = true,
                projectId,
                name = yamlDef.Name,
                strategy = yamlDef.Strategy,
                taskTemplate = yamlDef.TaskTemplate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project");
            return new { success = false, error = ex.Message };
        }
    }

    private static string GetIconForTemplate(TaskTemplate? template) => template switch
    {
        TaskTemplate.Summarize => "📋",
        TaskTemplate.Analyze => "🔍",
        TaskTemplate.Review => "📝",
        TaskTemplate.Critique => "🎭",
        TaskTemplate.Rewrite => "✏️",
        TaskTemplate.Continue => "📖",
        TaskTemplate.Extract => "🎯",
        TaskTemplate.Compare => "⚖️",
        TaskTemplate.QA => "❓",
        TaskTemplate.Translate => "🌐",
        TaskTemplate.CodeReview => "💻",
        TaskTemplate.Outline => "📑",
        _ => "🔬"
    };

    private static ReasoningOptions BuildOptions(ProjectConfig config)
    {
        return new ReasoningOptions
        {
            ProviderName = config.ProviderName,
            MaxLlmCalls = config.MaxLlmCalls ?? 500,
            MaxTokens = config.MaxTokens ?? 2_000_000,
            MaxDuration = TimeSpan.FromMinutes(config.MaxDurationMinutes ?? 30),
            MakerReliability = Enum.TryParse<MakerReliability>(config.Reliability, true, out var r) ? r : MakerReliability.Medium,
            UotDomainHint = config.DomainHint,
            UotMaxAnalogies = config.MaxAnalogies ?? 5,
            UotMaxCandidates = config.MaxCandidates ?? 10,
            EUotMaxOutsideThoughts = config.MaxOutsideThoughts ?? 10,
            EUotExplorationDirections = config.ExplorationDirections ?? 3,
            TUotMaxRuleSets = config.MaxRuleSets ?? 3,
            TUotMinRadicality = config.MinRadicality ?? 0.5f,
            Context = config.Context
        };
    }

    // ─────────────────────────────────────────────────────────
    //  文件上传管理
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 处理文件上传。
    /// </summary>
    public async Task<object> UploadFilesAsync(IFormFileCollection files, CancellationToken ct)
    {
        try
        {
            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
            Directory.CreateDirectory(uploadDir);

            var uploadedFiles = new List<object>();

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                var fileName = Path.GetFileName(file.FileName);
                var filePath = Path.Combine(uploadDir, fileName);

                await using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream, ct);

                uploadedFiles.Add(new
                {
                    name = fileName,
                    path = filePath,
                    size = file.Length
                });

                _logger.LogInformation("Uploaded file: {FileName} ({Size} bytes) to {UploadId}",
                    fileName, file.Length, uploadId);
            }

            return new
            {
                success = true,
                uploadId,
                files = uploadedFiles,
                expiresAt = DateTimeOffset.UtcNow.AddHours(24).ToString("O")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload files");
            return new { success = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 预览内容（不执行任务）。
    /// </summary>
    public async Task<object> PreviewContentAsync(string configJson, CancellationToken ct)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ProjectConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Content == null)
            {
                return new { success = false, error = "No content source specified" };
            }

            var contentSource = config.Content.ToContentSource();
            var preview = await _contentLoader.PreviewAsync(contentSource, ct);

            return new
            {
                success = true,
                fileCount = preview.FileCount,
                files = preview.FilePaths.Select(Path.GetFileName),
                totalSizeBytes = preview.TotalSizeBytes,
                estimatedTokens = preview.EstimatedTokens,
                exceedsLimits = preview.ExceedsLimits,
                warning = preview.LimitWarning
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview content");
            return new { success = false, error = ex.Message };
        }
    }

    public bool DeleteProject(string projectId)
    {
        return _projectStore.DeleteProject(projectId);
    }

    // ─────────────────────────────────────────────────────────
    //  运行管理
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartRunAsync(string projectId, CancellationToken ct)
    {
        var project = AllProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return new { success = false, error = "Project not found" };

        var strategy = _registry.Get(project.Strategy);
        if (strategy == null)
            return new { success = false, error = $"Strategy {project.Strategy} not available" };

        if (_runs.TryGetValue(projectId, out var existing) && existing.Status == MeshRunStatus.Running)
            return new { success = false, error = "Already running", runId = existing.RunId };

        var run = new MeshRun
        {
            ProjectId = projectId,
            ProjectName = project.Name,
            Strategy = project.Strategy
        };
        _runs[projectId] = run;

        // 设置输出目录
        var outputBase = Path.Combine(Directory.GetCurrentDirectory(), "output", projectId, run.RunId);
        Directory.CreateDirectory(outputBase);
        run.OutputDir = outputBase;

        _logger.LogInformation("Starting {Strategy} run {RunId} for project {Project}",
            project.Strategy, run.RunId, projectId);

        // 后台执行（使用可取消的 token）
        _ = ExecuteAsync(run, project, strategy, run.CancellationTokenSource.Token);

        return new { success = true, runId = run.RunId, strategy = project.Strategy.ToString() };
    }

    public object StopRun(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { success = false, error = "No run found" };

        if (run.Status != MeshRunStatus.Running)
            return new { success = false, error = "Run is not running" };

        try
        {
            run.CancellationTokenSource.Cancel();
            run.Status = MeshRunStatus.Failed;
            run.Error = "Stopped by user";
            
            // 发送停止事件
            run.EventChannel.Writer.TryWrite(new ErrorEvent
            {
                RunId = run.RunId,
                Message = "Run stopped by user"
            });
            run.EventChannel.Writer.TryComplete();

            _logger.LogInformation("Stopped run {RunId} for project {Project}", run.RunId, projectId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop run {RunId}", run.RunId);
            return new { success = false, error = ex.Message };
        }
    }

    private async Task ExecuteAsync(MeshRun run, MeshProject project, IReasoningStrategy strategy, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 创建进度回调
            var progress = new Progress<ReasoningProgress>(p =>
            {
                run.Timeline.Add(new TimelineEntry(p.Phase, p.Message ?? "", DateTimeOffset.UtcNow));

                // 更新运行状态
                run.CurrentPhase = p.Phase;
                run.ProgressPercent = p.ProgressPercent;
                run.Depth = p.Depth ?? run.Depth;
                run.TotalLlmCalls = p.TotalLlmCalls ?? run.TotalLlmCalls;
                run.TotalTokens = (p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0);

                // 发送 SSE 事件
                SendEvent(run, new ProgressEvent
                {
                    RunId = run.RunId,
                    Phase = p.Phase,
                    Message = p.Message,
                    TaskId = p.TaskId,
                    Depth = p.Depth,
                    ProgressPercent = p.ProgressPercent
                });

                // MAKER 特有事件
                if (p.Voting != null)
                {
                    SendEvent(run, new VotingEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        VotingType = p.Voting.Type,
                        Round = p.Voting.Round,
                        TotalVotes = p.Voting.TotalVotes,
                        VotesNeeded = p.Voting.VotesNeeded,
                        LeaderVotes = p.Voting.LeaderVotes,
                        RunnerUpVotes = p.Voting.RunnerUpVotes,
                        ClusterCount = p.Voting.ClusterCount,
                        UsedSemanticClustering = p.Voting.UsedSemanticClustering,
                        Success = p.Voting.LeaderVotes - p.Voting.RunnerUpVotes >= p.Voting.VotesNeeded
                    });
                }

                if (p.Proposal != null)
                {
                    SendEvent(run, new ProposalEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        Content = p.Proposal.Content,
                        Success = p.Proposal.Success,
                        Error = p.Proposal.Error,
                        PromptTokens = p.Proposal.PromptTokens,
                        CompletionTokens = p.Proposal.CompletionTokens,
                        ProviderName = p.Proposal.ProviderName
                    });
                }

                if (p.StreamingToken != null)
                {
                    SendEvent(run, new StreamingEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        WorkerId = p.StreamingToken.WorkerId,
                        ProposalId = p.StreamingToken.ProposalId,
                        Token = p.StreamingToken.Token,
                        AccumulatedContent = p.StreamingToken.AccumulatedContent,
                        TokenIndex = p.StreamingToken.TokenIndex,
                        IsFirstToken = p.StreamingToken.IsFirstToken,
                        IsLastToken = p.StreamingToken.IsLastToken,
                        ProviderName = p.StreamingToken.ProviderName,
                        SystemPrompt = p.StreamingToken.SystemPrompt,
                        UserPrompt = p.StreamingToken.UserPrompt
                    });
                }

                // Cognitive DSL 步骤事件
                if (!string.IsNullOrEmpty(p.StepId))
                {
                    SendEvent(run, new WorkflowStepMeshEvent
                    {
                        RunId = run.RunId,
                        StepId = p.StepId,
                        StepType = p.StepType ?? "",
                        Status = p.StepStatus ?? "Running",
                        Progress = p.ProgressPercent,
                        Message = p.Message,
                        Depth = p.Depth ?? 0,
                        VoteRound = p.VoteRound,
                        VoteMaxRounds = p.VoteMaxRounds,
                        VoteK = p.VoteK,
                        VoteCurrentVotes = p.VoteCurrentVotes,
                        ParallelTotal = p.ParallelTotal,
                        ParallelCompleted = p.ParallelCompleted,
                        ParallelFailed = p.ParallelFailed,
                        LlmCalls = p.TotalLlmCalls,
                        TokensUsed = (int?)((p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0)),
                        // LLM 对话记录
                        SystemPrompt = p.SystemPrompt,
                        UserPrompt = p.UserPrompt,
                        AssistantResponse = p.AssistantResponse
                    });
                }
            });

            // ───────────────────────────────────────────────────────
            //  动态加载任务内容
            // ───────────────────────────────────────────────────────
            var task = project.Task;
            var options = project.Options;

            // 如果有 ContentSource，加载内容
            if (project.ContentSource != null)
            {
                try
                {
                    var loadedContent = await _contentLoader.LoadAsync(project.ContentSource, ct);

                    if (!loadedContent.IsEmpty)
                    {
                        _logger.LogInformation("[{Project}] Loaded {FileCount} files, {Tokens} tokens",
                            run.ProjectId, loadedContent.FileCount, loadedContent.EstimatedTokens);

                        // 如果有 TaskDefinition，使用模板提示词
                        if (project.TaskDefinition != null)
                        {
                            var systemPrompt = TaskTemplatePrompts.GetSystemPrompt(project.TaskDefinition);
                            var userPrompt = TaskTemplatePrompts.GetUserPrompt(loadedContent, project.TaskDefinition);
                            task = $"{systemPrompt}\n\n{userPrompt}";
                        }
                        else
                        {
                            // 没有 TaskDefinition，直接将内容附加到原始 task
                            task = $"""
                                {project.Task}

                                ═══════════════════════════════════════════════════════════════
                                CONTENT ({loadedContent.FileCount} files, ~{loadedContent.EstimatedTokens} tokens)
                                ═══════════════════════════════════════════════════════════════

                                {loadedContent.CombinedText}

                                ═══════════════════════════════════════════════════════════════
                                END OF CONTENT
                                ═══════════════════════════════════════════════════════════════
                                """;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Project}] Failed to load content", run.ProjectId);
                    // 继续使用原始 task
                }
            }
            // 兼容：paper-review 特殊处理
            else if (project.Id == "paper-review")
            {
                // 尝试加载论文内容
                var paperPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "articles", "minimal_axiomatic_ontology_universe.md");
                if (File.Exists(paperPath))
                {
                    var paperContent = await File.ReadAllTextAsync(paperPath, ct);
                    task = $"""
                        Review and improve the following academic paper for top-tier AI conference publication.
                        
                        Provide specific, actionable suggestions for:
                        1. Structural improvements (organization, flow, section balance)
                        2. Argument clarity and logical rigor
                        3. Mathematical presentation and notation consistency
                        4. Related work coverage and positioning
                        5. Writing quality (clarity, conciseness, academic tone)
                        6. Figures and diagrams (if any)
                        
                        For each issue found:
                        - Quote the problematic text
                        - Explain why it's problematic
                        - Provide a concrete revision suggestion
                        
                        ═══════════════════════════════════════════════════════════════
                        PAPER TO REVIEW
                        ═══════════════════════════════════════════════════════════════
                        
                        {paperContent}
                        
                        ═══════════════════════════════════════════════════════════════
                        END OF PAPER
                        ═══════════════════════════════════════════════════════════════
                        """;
                    
                    _logger.LogInformation("[paper-review] Loaded paper content ({Length} chars)", paperContent.Length);
                }
                else
                {
                    _logger.LogWarning("[paper-review] Paper file not found at {Path}", paperPath);
                }
            }

            // 执行策略
            var result = await strategy.ExecuteAsync(task, options, progress, ct);

            stopwatch.Stop();
            run.Result = result;
            run.Status = result.Success ? MeshRunStatus.Completed : MeshRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            run.TotalLlmCalls = result.TotalLlmCalls;
            run.TotalTokens = result.TotalTokens;

            _logger.LogInformation("[{Project}] {Strategy} completed. Success={Success}, Duration={Duration}s",
                run.ProjectId, run.Strategy, result.Success, stopwatch.Elapsed.TotalSeconds);

            // 保存最终结果
            if (!string.IsNullOrEmpty(result.Content))
            {
                SaveFile(run, "artifacts", "final_report.md", $"""
                    # 📄 Final Report

                    **Project:** {run.ProjectName}  
                    **Strategy:** {run.Strategy.GetDisplayName()}  
                    **Run ID:** {run.RunId}  
                    **Status:** {(result.Success ? "✓ Success" : "✗ Failed")}  
                    **Duration:** {stopwatch.Elapsed.TotalSeconds:F1}s  
                    **LLM Calls:** {result.TotalLlmCalls}  
                    **Total Tokens:** {result.TotalTokens:N0}

                    ---

                    {result.Content}
                    """);
            }

            // 发送结果事件
            SendEvent(run, new ResultEvent
            {
                RunId = run.RunId,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalTokens = result.TotalTokens,
                PromptTokens = (int)result.PromptTokens,
                CompletionTokens = (int)result.CompletionTokens,
                TotalLlmCalls = result.TotalLlmCalls
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            run.Status = MeshRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            run.Error = ex.Message;

            _logger.LogError(ex, "[{Project}] {Strategy} failed", run.ProjectId, run.Strategy);

            SendEvent(run, new ErrorEvent
            {
                RunId = run.RunId,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            // 使用 TryComplete 避免与 StopRun 的竞态条件
            // Channel 可能已经被 StopRun 关闭，TryComplete 是幂等的
            run.EventChannel.Writer.TryComplete();
            
            // 保存运行历史和文件到磁盘
            SaveRunToHistory(run);
        }
    }

    /// <summary>
    /// 保存运行到历史记录和磁盘。
    /// </summary>
    private void SaveRunToHistory(MeshRun run)
    {
        try
        {
            // 创建项目输出目录
            var projectOutputDir = Path.Combine(_outputBasePath, run.ProjectId);
            var runOutputDir = Path.Combine(projectOutputDir, run.RunId);
            Directory.CreateDirectory(runOutputDir);

            // 保存文件到磁盘
            foreach (var (category, files) in run.Files)
            {
                var categoryDir = Path.Combine(runOutputDir, category);
                Directory.CreateDirectory(categoryDir);
                
                foreach (var (filename, content) in files)
                {
                    var filePath = Path.Combine(categoryDir, filename);
                    File.WriteAllText(filePath, content);
                }
            }

            // 保存时间线
            var timelineJson = JsonSerializer.Serialize(run.Timeline, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(runOutputDir, "timeline.json"), timelineJson);

            // 创建运行历史条目
            var historyEntry = new RunHistoryEntry
            {
                RunId = run.RunId,
                StartedAt = DateTimeOffset.Now - run.Duration,
                CompletedAt = DateTimeOffset.Now,
                Status = run.Status,
                Duration = run.Duration,
                TotalLlmCalls = run.TotalLlmCalls,
                TotalTokens = run.TotalTokens,
                Error = run.Error,
                OutputDir = runOutputDir,
                FileCount = run.Files.Sum(c => c.Value.Count)
            };

            // 添加到历史列表
            _runHistory.AddOrUpdate(
                run.ProjectId,
                _ => [historyEntry],
                (_, list) => { list.Add(historyEntry); return list; });

            _logger.LogInformation("Saved run {RunId} to history. Output: {Dir}", run.RunId, runOutputDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save run {RunId} to history", run.RunId);
        }
    }

    private void SendEvent(MeshRun run, MeshEvent evt)
    {
        run.EventChannel.Writer.TryWrite(evt);
    }

    private void SaveFile(MeshRun run, string category, string fileName, string content)
    {
        run.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (!string.IsNullOrEmpty(run.OutputDir))
        {
            try
            {
                var dir = Path.Combine(run.OutputDir, category);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write file: {Category}/{FileName}", category, fileName);
            }
        }

        SendEvent(run, new FileEvent
        {
            RunId = run.RunId,
            Category = category,
            FileName = fileName,
            Message = $"Generated: {category}/{fileName}"
        });
    }

    // ─────────────────────────────────────────────────────────
    //  状态查询
    // ─────────────────────────────────────────────────────────

    public object? GetStatus(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle", runId = (string?)null };

        return new
        {
            status = run.Status.ToString().ToLower(),
            runId = run.RunId,
            strategy = run.Strategy.ToString(),
            elapsed = run.Duration.TotalSeconds,
            progress = run.ProgressPercent,
            phase = run.CurrentPhase,
            totalTokens = run.TotalTokens,
            llmCalls = run.TotalLlmCalls,
            depth = run.CurrentDepth
        };
    }

    public object? GetSnapshot(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle" };

        return new
        {
            status = run.Status.ToString().ToLower(),
            runId = run.RunId,
            strategy = run.Strategy.ToString(),
            strategyDisplayName = run.Strategy.GetDisplayName(),
            result = run.Result?.Content,
            success = run.Result?.Success,
            llmCalls = run.TotalLlmCalls,
            totalTokens = run.TotalTokens,
            duration = run.Duration.TotalSeconds,
            depth = run.Depth,
            phase = run.CurrentPhase,
            progress = run.ProgressPercent
        };
    }

    public IEnumerable<object> GetTimeline(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        return run.Timeline.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp });
    }

    // ─────────────────────────────────────────────────────────
    //  运行历史 API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 获取项目的运行历史。
    /// </summary>
    public IEnumerable<object> GetRuns(string projectId)
    {
        var runs = new List<object>();

        // 当前运行（如果有）
        if (_runs.TryGetValue(projectId, out var currentRun))
        {
            runs.Add(new
            {
                runId = currentRun.RunId,
                startedAt = DateTimeOffset.Now - currentRun.Duration,
                completedAt = (DateTimeOffset?)null,
                status = currentRun.Status.ToString(),
                duration = currentRun.Duration.TotalSeconds,
                totalLlmCalls = currentRun.TotalLlmCalls,
                totalTokens = currentRun.TotalTokens,
                error = currentRun.Error,
                fileCount = currentRun.Files.Sum(c => c.Value.Count),
                isCurrent = true
            });
        }

        // 历史运行
        if (_runHistory.TryGetValue(projectId, out var history))
        {
            runs.AddRange(history.OrderByDescending(h => h.CompletedAt).Select(h => new
            {
                runId = h.RunId,
                startedAt = h.StartedAt,
                completedAt = (DateTimeOffset?)h.CompletedAt,
                status = h.Status.ToString(),
                duration = h.Duration.TotalSeconds,
                totalLlmCalls = h.TotalLlmCalls,
                totalTokens = h.TotalTokens,
                error = h.Error,
                fileCount = h.FileCount,
                isCurrent = false
            }));
        }

        // 扫描磁盘上的历史（如果内存中没有）
        var projectOutputDir = Path.Combine(_outputBasePath, projectId);
        if (Directory.Exists(projectOutputDir))
        {
            var existingRunIds = runs.Select(r => ((dynamic)r).runId).ToHashSet();
            
            foreach (var runDir in Directory.GetDirectories(projectOutputDir))
            {
                var runId = Path.GetFileName(runDir);
                if (existingRunIds.Contains(runId)) continue;

                var timelinePath = Path.Combine(runDir, "timeline.json");
                var fileCount = 0;
                
                // 统计文件数
                foreach (var catDir in Directory.GetDirectories(runDir))
                {
                    fileCount += Directory.GetFiles(catDir).Length;
                }

                var completedAt = Directory.GetLastWriteTime(runDir);
                
                runs.Add(new
                {
                    runId,
                    startedAt = (DateTimeOffset?)null,
                    completedAt = (DateTimeOffset?)new DateTimeOffset(completedAt),
                    status = "Completed",
                    duration = 0.0,
                    totalLlmCalls = 0,
                    totalTokens = 0L,
                    error = (string?)null,
                    fileCount,
                    isCurrent = false
                });
            }
        }

        return runs.OrderByDescending(r => ((dynamic)r).completedAt ?? DateTimeOffset.MaxValue);
    }

    /// <summary>
    /// 获取特定运行的文件列表。
    /// </summary>
    public IEnumerable<object> GetRunFiles(string projectId, string runId)
    {
        var files = new List<object>();

        // 检查是否是当前运行
        if (_runs.TryGetValue(projectId, out var currentRun) && currentRun.RunId == runId)
        {
            foreach (var (category, fileDict) in currentRun.Files)
            {
                foreach (var name in fileDict.Keys)
                {
                    files.Add(new { category, name, path = $"{category}/{name}", source = "memory" });
                }
            }
            return files.OrderBy(f => ((dynamic)f).category).ThenBy(f => ((dynamic)f).name);
        }

        // 从磁盘读取
        var runDir = Path.Combine(_outputBasePath, projectId, runId);
        if (!Directory.Exists(runDir))
            return files;

        foreach (var catDir in Directory.GetDirectories(runDir))
        {
            var category = Path.GetFileName(catDir);
            foreach (var filePath in Directory.GetFiles(catDir))
            {
                var name = Path.GetFileName(filePath);
                files.Add(new { category, name, path = $"{category}/{name}", source = "disk" });
            }
        }

        return files.OrderBy(f => ((dynamic)f).category).ThenBy(f => ((dynamic)f).name);
    }

    /// <summary>
    /// 获取特定运行的文件内容。
    /// </summary>
    public string? GetRunFileContent(string projectId, string runId, string category, string name)
    {
        // 检查是否是当前运行
        if (_runs.TryGetValue(projectId, out var currentRun) && currentRun.RunId == runId)
        {
            if (currentRun.Files.TryGetValue(category, out var files))
            {
                return files.GetValueOrDefault(name);
            }
        }

        // 从磁盘读取
        var filePath = Path.Combine(_outputBasePath, projectId, runId, category, name);
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath);
        }

        return null;
    }

    /// <summary>
    /// 获取特定运行的时间线。
    /// </summary>
    public IEnumerable<object> GetRunTimeline(string projectId, string runId)
    {
        // 检查是否是当前运行
        if (_runs.TryGetValue(projectId, out var currentRun) && currentRun.RunId == runId)
        {
            return currentRun.Timeline.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp });
        }

        // 从磁盘读取
        var timelinePath = Path.Combine(_outputBasePath, projectId, runId, "timeline.json");
        if (File.Exists(timelinePath))
        {
            try
            {
                var json = File.ReadAllText(timelinePath);
                var timeline = JsonSerializer.Deserialize<List<TimelineEntry>>(json);
                return timeline?.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp }) ?? [];
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    public async IAsyncEnumerable<MeshEvent> GetEventStreamAsync(
        string projectId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            yield break;

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    public IEnumerable<object> GetFiles(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        var files = new List<object>();
        foreach (var (category, fileDict) in run.Files)
        {
            foreach (var name in fileDict.Keys)
            {
                files.Add(new { category, name, path = $"{category}/{name}" });
            }
        }
        return files.OrderBy(f => ((dynamic)f).category).ThenBy(f => ((dynamic)f).name);
    }

    public string? GetFileContent(string projectId, string category, string name)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return null;

        if (!run.Files.TryGetValue(category, out var files))
            return null;

        return files.GetValueOrDefault(name);
    }

    // ─────────────────────────────────────────────────────────
    //  示例问题
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetSampleProblems() =>
        AllProjects
            .Where(p => p.Strategy.IsUoT())
            .Select(p => new
            {
                p.Id,
                title = p.Name,
                problem = p.Task,
                domainHint = p.Options.UotDomainHint,
                strategy = p.Strategy.ToString(),
                strategyDisplayName = p.Strategy.GetDisplayName(),
                strategyDescription = p.Strategy.GetDescription(),
                category = p.Strategy switch
                {
                    StrategyKind.UotCombinational => "Combinational",
                    StrategyKind.UotExploratory => "Exploratory",
                    StrategyKind.UotTransformative => "Transformative",
                    _ => "Creative"
                }
            });
}

