using System.Collections.Concurrent;
using System.Diagnostics;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Maker;
using Aevatar.Agents.AI.MEAI.Telemetry;
using MakerSystem.Projects;

namespace MakerSystem;

// ============================================================
//  Project Service - Clean, Simple, ~150 lines
//  Compare to ~400 lines across multiple files in V1
// ============================================================

/// <summary>
/// Project definition.
/// </summary>
public sealed record ProjectDef(
    string Id,
    string Name,
    string Description,
    string Icon,
    string Task,  // The actual task to execute
    Func<MakerOptions> BuildOptions);

/// <summary>
/// Run state for a project.
/// </summary>
public sealed class ProjectRun
{
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Status { get; set; } = "running";
    public MakerResult? Result { get; set; }
    public List<ProgressEntry> Timeline { get; } = [];
    public HashSet<string> WorkerIds { get; } = [];
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
    
    // SSE: Thread-safe event channel
    public System.Threading.Channels.Channel<SSEEvent> EventChannel { get; } = 
        System.Threading.Channels.Channel.CreateUnbounded<SSEEvent>();
    
    // Process files: category -> filename -> content
    public Dictionary<string, Dictionary<string, string>> Files { get; } = new()
    {
        ["proposals"] = new(),      // LLM proposals (decomposition/solution)
        ["votes"] = new(),          // Voting results
        ["consensus"] = new(),      // Consensus analysis
        ["artifacts"] = new()       // Stage results and final result
    };
    
    // Disk storage path
    public string? OutputDir { get; set; }
    
    // Task configuration for display
    public RunConfig? Config { get; set; }
    
    // Track all proposals per task for comparison
    public Dictionary<string, List<ProposalRecord>> ProposalsByTask { get; } = new();
}

/// <summary>
/// Record of a proposal for analysis.
/// </summary>
public sealed record ProposalRecord(
    string ProposalId,
    string Content,
    bool Success,
    string? Error,
    DateTimeOffset Timestamp,
    string? ProviderName = null
);

/// <summary>
/// File info for frontend.
/// </summary>
public sealed record FileInfo(string Category, string Name, string Path);

public sealed record ProgressEntry(string Phase, string Message, DateTimeOffset Timestamp);

/// <summary>
/// Task configuration snapshot for frontend display.
/// </summary>
public sealed record RunConfig
{
    // Basic config
    public required string ProjectName { get; init; }
    public required string ProjectDescription { get; init; }
    public required string Task { get; init; }
    public required string Reliability { get; init; }
    public required int ConsensusK { get; init; }
    public required int SamplesPerRound { get; init; }
    public required double StepTimeoutSeconds { get; init; }
    
    // Budget-based limits (replaces MaxDepth)
    // MAKER trades tokens for correctness - don't be stingy!
    public int MaxTotalLlmCalls { get; init; } = 500;
    public long MaxTotalTokens { get; init; } = 2_000_000;
    public int MaxDurationMinutes { get; init; } = 30;
    
    // Execution mode (Production vs Academic)
    public string ExecutionMode { get; init; } = "Production";
    public string Granularity { get; init; } = "Balanced";
    public int HardDepthCap { get; init; } = 50;
    
    // Strategy types
    public string? DecomposerType { get; init; }
    public string? SolverType { get; init; }
    public string? ComposerType { get; init; }
    
    // Context
    public IReadOnlyDictionary<string, string>? Context { get; init; }
    
    // Advanced MAKER parameters
    public required string ClusteringMethod { get; init; }
    public required float SemanticSimilarityThreshold { get; init; }
    public required float TemperatureVariance { get; init; }
    public required float BaseTemperature { get; init; }
    public required bool UseMultipleProviders { get; init; }
    public required int RedFlagThreshold { get; init; }
}

/// <summary>
/// SSE event for real-time streaming.
/// </summary>
public sealed record SSEEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public required string Type { get; init; }  // "progress", "proposal", "voting", "result", "error", "redFlag"
    
    [System.Text.Json.Serialization.JsonPropertyName("taskId")]
    public required string TaskId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("phase")]
    public string? Phase { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string? Content { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool? Success { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("depth")]
    public int? Depth { get; init; }
    
    // Voting progress
    [System.Text.Json.Serialization.JsonPropertyName("votingType")]
    public string? VotingType { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("round")]
    public int? Round { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("totalVotes")]
    public int? TotalVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("votesNeeded")]
    public int? VotesNeeded { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("leaderVotes")]
    public int? LeaderVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("runnerUpVotes")]
    public int? RunnerUpVotes { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("clusterCount")]
    public int? ClusterCount { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("usedSemanticClustering")]
    public bool? UsedSemanticClustering { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("earlyTermination")]
    public bool? EarlyTermination { get; init; }
    
    // Token telemetry
    [System.Text.Json.Serialization.JsonPropertyName("promptTokens")]
    public int? PromptTokens { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("completionTokens")]
    public int? CompletionTokens { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("totalTokens")]
    public long? TotalTokens { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("totalLlmCalls")]
    public int? TotalLlmCalls { get; init; }
    
    // Red flag
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string? Reason { get; init; }
    
    // LLM provider info
    [System.Text.Json.Serialization.JsonPropertyName("providerName")]
    public string? ProviderName { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    // Streaming token fields (for real-time LLM output)
    [System.Text.Json.Serialization.JsonPropertyName("workerId")]
    public string? WorkerId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("proposalId")]
    public string? ProposalId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("token")]
    public string? Token { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("accumulatedContent")]
    public string? AccumulatedContent { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("tokenIndex")]
    public int? TokenIndex { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("isFirstToken")]
    public bool? IsFirstToken { get; init; }
    
    // Chat context (for SYSTEM_NODES chat display)
    [System.Text.Json.Serialization.JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("userPrompt")]
    public string? UserPrompt { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("isLastToken")]
    public bool? IsLastToken { get; init; }
    
    // Trace events (Aspire-style real-time telemetry)
    [System.Text.Json.Serialization.JsonPropertyName("traceId")]
    public string? TraceId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("spanId")]
    public string? SpanId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("operationName")]
    public string? OperationName { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("durationMs")]
    public double? DurationMs { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; init; }
    
    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; init; }
}

/// <summary>
/// Main service managing all projects.
/// </summary>
public sealed class MakerProjectService
{
    private readonly IMakerExecutor _executor;
    private readonly ILogger<MakerProjectService> _logger;
    private readonly IExecutionTraceStore _traceStore;
    private readonly ConcurrentDictionary<string, ProjectRun> _runs = new();
    
    // Dynamic projects added via API (config-based, zero-code)
    private readonly ConcurrentDictionary<string, ProjectDef> _dynamicProjects = new();

    // Built-in projects (code-based strategies)
    private static readonly ProjectDef[] BuiltInProjects =
    [
        // new("paper", "论文总结", "多 Agent 协作拆解技术论文，生成结构化总结。", "📄",
        //     $"Summarize the paper '{PaperContent.Title}' by decomposing it into logical sections.",  // Task
        //     () => new MakerOptions
        //     {
        //         Reliability = ReliabilityLevel.Medium,
        //         Decomposer = new PaperDecomposer(),
        //         Solver = new PaperSolver(),
        //         MaxTotalLlmCalls = 100,
        //         MaxTotalTokens = 500_000,
        //         Mode = ExecutionMode.Academic,
        //         Granularity = DecompositionGranularity.Single,
        //         // Multi-provider: auto-discovers all valid providers from config
        //         UseMultipleProviders = true,
        //         //CoordinatorProviderName = "openai"
        //     }),
        
        // Paper Review Project - Multi-agent collaborative paper improvement
        new("paper-review", "论文审稿", "多 Agent 协作审阅论文，提供修改建议直到达到发表水平。", "📝",
            PaperReviewProject.Create(
                paperPath: "/Users/zhaoyiqi/Code/aevatar-agent-framework/articles/minimal_axiomatic_ontology_universe.md",  // ⚙️ Configure your paper path here
                targetVenue: "Top-tier AI Conference",
                language: "English"
            ).Task,
            () => PaperReviewProject.Create(
                paperPath: "/Users/zhaoyiqi/Code/aevatar-agent-framework/articles/minimal_axiomatic_ontology_universe.md",  // ⚙️ Configure your paper path here
                targetVenue: "Top-tier AI Conference",
                language: "English"
            ).BuildOptions())
    ];

    // All projects (built-in + dynamic)
    private IEnumerable<ProjectDef> AllProjects => BuiltInProjects.Concat(_dynamicProjects.Values);

    public MakerProjectService(
        IMakerExecutor executor,
        ILogger<MakerProjectService> logger,
        IExecutionTraceStore traceStore)
    {
        _executor = executor;
        _logger = logger;
        _traceStore = traceStore;
    }
    
    // ============================================================
    //  Dynamic Project Management (Zero-Code Config)
    // ============================================================
    
    /// <summary>
    /// Create a new project from JSON config.
    /// </summary>
    public object CreateProjectFromConfig(string configJson)
    {
        try
        {
            var config = ProjectConfig.FromJson(configJson);
            var projectId = $"custom_{Guid.NewGuid():N}"[..16];
            
            var project = new ProjectDef(
                projectId,
                config.Name,
                config.Description,
                config.Icon,
                config.Task,  // Pass the task from config
                () => config.BuildOptions());
            
            _dynamicProjects[projectId] = project;
            
            _logger.LogInformation("Created dynamic project: {ProjectId} - {Name}, Task: {Task}", 
                projectId, config.Name, config.Task[..Math.Min(50, config.Task.Length)]);
            
            return new
            {
                success = true,
                projectId,
                name = config.Name,
                description = config.Description,
                icon = config.Icon
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project from config");
            return new { success = false, error = ex.Message };
        }
    }
    
    /// <summary>
    /// Delete a dynamic project.
    /// </summary>
    public bool DeleteProject(string projectId)
    {
        // Cannot delete built-in projects
        if (BuiltInProjects.Any(p => p.Id == projectId))
        {
            return false;
        }
        return _dynamicProjects.TryRemove(projectId, out _);
    }
    
    /// <summary>
    /// Get sample config templates for different use cases.
    /// </summary>
    public static object GetConfigTemplates() => new
    {
        simple = new ProjectConfig
        {
            Name = "My Analysis Project",
            Description = "A simple analysis task",
            Icon = "🔬",
            Task = "Analyze the following data and provide insights...",
            Reliability = "Medium",
            MaxTotalLlmCalls = 50,
            MaxTotalTokens = 200_000,
            UseMultipleProviders = false,  // Single provider mode
            Context = new Dictionary<string, string>
            {
                ["domain"] = "data analysis",
                ["output_format"] = "markdown report"
            }
        },
        advanced = new ProjectConfig
        {
            Name = "Custom Research Project",
            Description = "Complex multi-step research with custom prompts",
            Icon = "🧪",
            Task = "Research and synthesize information about...",
            Reliability = "High",
            MaxTotalLlmCalls = 100,
            MaxTotalTokens = 500_000,
            UseMultipleProviders = true,  // Auto-discover all valid providers
            CoordinatorProviderName = null,  // null = Coordinator joins round-robin
            Context = new Dictionary<string, string>
            {
                ["language"] = "Chinese",
                ["depth"] = "comprehensive"
            },
            Decomposition = new DecompositionConfig
            {
                PromptTemplate = """
                    You are a research methodology expert.
                    
                    Break down this research task into 3-5 distinct phases:
                    {task}
                    
                    Context: {context}
                    
                    Output as JSON: [{"step_id": "P1", "description": "..."}]
                    """,
                MinDepthForAtomic = 2,
                AtomicKeywords = ["summarize", "conclude", "finalize"]
            },
            Solution = new SolutionConfig
            {
                PromptTemplate = """
                    You are a domain expert. Complete this research task:
                    {task}
                    
                    Context: {context}
                    
                    Provide a thorough, well-structured response.
                    """,
                OutputFormat = "markdown"
            }
        }
    };

    /// <summary>
    /// Record task tree results as artifacts.
    /// </summary>
    private void RecordTaskTree(ProjectRun run, TaskNode node, int depth)
    {
        var depthPrefix = depth == 0 ? "root" : $"D{depth}";
        var fileName = $"{depthPrefix}_{node.TaskId}_result.md";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 📋 Task Result: {node.TaskId}");
        sb.AppendLine();
        sb.AppendLine("## Metadata");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|-------|-------|");
        sb.AppendLine($"| **Task ID** | `{node.TaskId}` |");
        sb.AppendLine($"| **Recursion Depth** | D{depth} |");
        sb.AppendLine($"| **Type** | {(node.IsAtomic ? "🎯 Atomic (Solved Directly)" : "🔀 Composite (Decomposed)")} |");
        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(node.Description);
        sb.AppendLine();
        
        // Voting sessions
        if (node.VotingSessions.Count > 0)
        {
            sb.AppendLine("## Voting Sessions");
            sb.AppendLine();
            foreach (var session in node.VotingSessions)
            {
                sb.AppendLine($"### {session.Type} ({session.Rounds} rounds, {session.Candidates.Count} candidates)");
                sb.AppendLine();
                
                if (session.Winner != null)
                {
                    sb.AppendLine($"**✓ Winner:** {session.Winner.Votes} votes");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(session.Winner.Content);
                    sb.AppendLine("```");
                }
                
                // Show other candidates for comparison
                var others = session.Candidates.Where(c => c.Hash != session.Winner?.Hash).Take(3).ToList();
                if (others.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("**Other candidates (for comparison):**");
                    foreach (var other in others)
                    {
                        var preview = other.Content.Length > 100 ? other.Content[..100] + "..." : other.Content;
                        sb.AppendLine($"- ({other.Votes} votes) `{preview.Replace("\n", " ")}`");
                    }
                }
                sb.AppendLine();
            }
        }
        
        // Result
        if (!string.IsNullOrEmpty(node.Result))
        {
            sb.AppendLine("## Final Result");
            sb.AppendLine();
            sb.AppendLine(node.Result);
        }
        
        // Children
        if (node.Children.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Child Tasks ({node.Children.Count})");
            sb.AppendLine();
            sb.AppendLine("| Depth | Task ID | Type | Description |");
            sb.AppendLine("|-------|---------|------|-------------|");
            foreach (var child in node.Children)
            {
                var childType = child.IsAtomic ? "Atomic" : "Composite";
                var desc = child.Description.Length > 50 ? child.Description[..50] + "..." : child.Description;
                sb.AppendLine($"| D{depth + 1} | `{child.TaskId}` | {childType} | {desc} |");
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Task execution trace for MAKER consensus analysis.*");
        
        SaveFile(run, "artifacts", fileName, sb.ToString());
        
        // Recurse into children
        foreach (var child in node.Children)
        {
            RecordTaskTree(run, child, depth + 1);
        }
    }

    public IEnumerable<object> GetProjects() =>
        AllProjects.Select(p => new 
        { 
            p.Id, 
            p.Name, 
            p.Description, 
            p.Icon,
            isDynamic = _dynamicProjects.ContainsKey(p.Id)
        });

    public object? GetStatus(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle", runId = (string?)null };

        return new
        {
            status = run.Status,
            runId = run.RunId,
            elapsed = run.Stopwatch.Elapsed.TotalSeconds
        };
    }

    public object? GetSnapshot(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle" };

        return new
        {
            status = run.Status,
            runId = run.RunId,
            result = run.Result?.Content,
            finalResult = run.Result?.Content,
            success = run.Result?.Success,
            llmCalls = run.Result?.TotalLLMCalls,
            duration = run.Result?.Duration.TotalSeconds,
            workerIds = run.WorkerIds.ToArray(),
            // Task configuration details
            config = run.Config == null ? null : new
            {
                projectName = run.Config.ProjectName,
                projectDescription = run.Config.ProjectDescription,
                task = run.Config.Task,
                reliability = run.Config.Reliability,
                consensusK = run.Config.ConsensusK,
                samplesPerRound = run.Config.SamplesPerRound,
                stepTimeoutSeconds = run.Config.StepTimeoutSeconds,
                // Budget-based limits
                maxTotalLlmCalls = run.Config.MaxTotalLlmCalls,
                maxTotalTokens = run.Config.MaxTotalTokens,
                maxDurationMinutes = run.Config.MaxDurationMinutes,
                // Execution mode
                executionMode = run.Config.ExecutionMode,
                granularity = run.Config.Granularity,
                hardDepthCap = run.Config.HardDepthCap,
                decomposerType = run.Config.DecomposerType,
                solverType = run.Config.SolverType,
                composerType = run.Config.ComposerType,
                context = run.Config.Context,
                // Advanced MAKER parameters
                clusteringMethod = run.Config.ClusteringMethod,
                semanticSimilarityThreshold = run.Config.SemanticSimilarityThreshold,
                temperatureVariance = run.Config.TemperatureVariance,
                baseTemperature = run.Config.BaseTemperature,
                useMultipleProviders = run.Config.UseMultipleProviders,
                redFlagThreshold = run.Config.RedFlagThreshold
            }
        };
    }

    public IEnumerable<object> GetTimeline(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        return run.Timeline.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp });
    }

    /// <summary>
    /// Get SSE event stream for a project.
    /// </summary>
    public async IAsyncEnumerable<SSEEvent> GetEventStreamAsync(
        string projectId, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            yield break;

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Get list of generated files.
    /// </summary>
    public IEnumerable<FileInfo> GetFiles(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        var files = new List<FileInfo>();
        foreach (var (category, fileDict) in run.Files)
        {
            foreach (var name in fileDict.Keys)
            {
                files.Add(new FileInfo(category, name, $"{category}/{name}"));
            }
        }
        return files.OrderBy(f => f.Category).ThenBy(f => f.Name);
    }

    /// <summary>
    /// Get file content.
    /// </summary>
    public string? GetFileContent(string projectId, string category, string name)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return null;

        if (!run.Files.TryGetValue(category, out var files))
            return null;

        return files.GetValueOrDefault(name);
    }

    /// <summary>
    /// Save a file to memory and disk.
    /// </summary>
    private void SaveFile(ProjectRun run, string category, string fileName, string content)
    {
        // Save to memory
        run.Files[category][fileName] = content;
        
        // Save to disk
        if (!string.IsNullOrEmpty(run.OutputDir))
        {
            try
            {
                var dir = Path.Combine(run.OutputDir, category);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, fileName);
                File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write file to disk: {Category}/{FileName}", category, fileName);
            }
        }
        
        // Send SSE event
        run.EventChannel.Writer.TryWrite(new SSEEvent
        {
            Type = "file",
            TaskId = fileName,
            Phase = category,
            Message = $"Generated: {category}/{fileName}",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Generate consensus analysis file when voting completes.
    /// </summary>
    private void GenerateConsensusAnalysis(ProjectRun run, string taskId, string votingType, int depth)
    {
        if (!run.ProposalsByTask.TryGetValue(taskId, out var proposals) || proposals.Count == 0)
            return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 📊 Consensus Analysis: {taskId}");
        sb.AppendLine();
        sb.AppendLine($"**Type:** {votingType}  ");
        sb.AppendLine($"**Depth:** D{depth}  ");
        sb.AppendLine($"**Total Proposals:** {proposals.Count}  ");
        sb.AppendLine($"**Time:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        
        // Group by content hash for comparison
        var grouped = proposals
            .Where(p => p.Success && !string.IsNullOrEmpty(p.Content))
            .GroupBy(p => p.Content.GetHashCode())
            .OrderByDescending(g => g.Count())
            .ToList();

        // Provider distribution summary
        var providerGroups = proposals
            .Where(p => !string.IsNullOrEmpty(p.ProviderName))
            .GroupBy(p => p.ProviderName)
            .ToList();
        
        if (providerGroups.Count > 0)
        {
            sb.AppendLine("## Provider Distribution");
            sb.AppendLine();
            sb.AppendLine("| Provider | Proposals | Success Rate |");
            sb.AppendLine("|----------|-----------|--------------|");
            foreach (var pg in providerGroups)
            {
                var total = pg.Count();
                var success = pg.Count(x => x.Success);
                var rate = total > 0 ? (success * 100.0 / total) : 0;
                sb.AppendLine($"| 🤖 **{pg.Key}** | {total} | {rate:F0}% |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Proposal Distribution");
        sb.AppendLine();
        sb.AppendLine("| Cluster | Votes | Provider | First Seen | Content Preview |");
        sb.AppendLine("|---------|-------|----------|------------|-----------------|");
        
        var clusterNum = 1;
        foreach (var group in grouped)
        {
            var first = group.First();
            // Increased preview length from 80 to 200 chars for better visibility
            var preview = first.Content.Length > 200 ? first.Content[..200] + "..." : first.Content;
            preview = preview.Replace("\n", " ").Replace("|", "\\|");
            var provider = string.IsNullOrEmpty(first.ProviderName) ? "-" : first.ProviderName;
            sb.AppendLine($"| C{clusterNum} | {group.Count()} | {provider} | {first.Timestamp:HH:mm:ss} | {preview} |");
            clusterNum++;
        }
        
        sb.AppendLine();
        sb.AppendLine("## All Proposals (Chronological)");
        sb.AppendLine();
        
        foreach (var (p, idx) in proposals.Select((p, i) => (p, i)))
        {
            var status = p.Success ? "✓" : "✗";
            var providerLabel = string.IsNullOrEmpty(p.ProviderName) ? "" : $" 🤖 {p.ProviderName}";
            sb.AppendLine($"### Proposal {idx + 1}: {p.ProposalId} {status}{providerLabel}");
            sb.AppendLine();
            sb.AppendLine($"**Time:** {p.Timestamp:HH:mm:ss.fff}");
            if (!string.IsNullOrEmpty(p.ProviderName))
            {
                sb.AppendLine($"**Provider:** {p.ProviderName}");
            }
            sb.AppendLine();
            
            if (p.Success && !string.IsNullOrEmpty(p.Content))
            {
                sb.AppendLine("```");
                sb.AppendLine(p.Content);
                sb.AppendLine("```");
            }
            else if (!string.IsNullOrEmpty(p.Error))
            {
                sb.AppendLine($"**Error:** {p.Error}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("---");
        sb.AppendLine("*This analysis helps understand how LLM proposals converged to consensus.*");
        
        var fileName = $"consensus_{taskId}_{votingType}.md";
        SaveFile(run, "consensus", fileName, sb.ToString());
    }

    public async Task<object> StartRunAsync(string projectId, CancellationToken ct)
    {
        var project = AllProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return new { success = false, error = "Project not found" };

        if (_runs.TryGetValue(projectId, out var existing) && existing.Status == "running")
            return new { success = false, error = "Already running", runId = existing.RunId };

        var run = new ProjectRun();
        _runs[projectId] = run;
        
        // Setup disk output directory
        var outputBase = Path.Combine(Directory.GetCurrentDirectory(), "output", projectId, run.RunId);
        Directory.CreateDirectory(outputBase);
        run.OutputDir = outputBase;

        _logger.LogInformation("Starting {Project} run {RunId}, output: {OutputDir}", projectId, run.RunId, outputBase);
        run.Timeline.Add(new ProgressEntry("Starting", $"开始执行 {project.Name}", DateTimeOffset.UtcNow));

        // Get task description from project definition
        var taskDescription = project.Task;

        var options = project.BuildOptions();
        
        // Save configuration for frontend display
        run.Config = new RunConfig
        {
            ProjectName = project.Name,
            ProjectDescription = project.Description,
            Task = taskDescription,
            Reliability = options.Reliability.ToString(),
            ConsensusK = options.ConsensusK,
            SamplesPerRound = options.SamplesPerRound,
            StepTimeoutSeconds = options.StepTimeout.TotalSeconds,
            // Budget-based limits
            MaxTotalLlmCalls = options.MaxTotalLlmCalls,
            MaxTotalTokens = options.MaxTotalTokens,
            MaxDurationMinutes = (int)options.MaxDuration.TotalMinutes,
            // Execution mode
            ExecutionMode = options.Mode.ToString(),
            Granularity = options.Granularity.ToString(),
            HardDepthCap = options.HardDepthCap,
            DecomposerType = options.Decomposer?.GetType().Name ?? "DefaultDecomposer",
            SolverType = options.Solver?.GetType().Name ?? "DefaultSolver",
            ComposerType = options.Composer?.GetType().Name ?? "DefaultComposer",
            Context = options.Context,
            // Advanced MAKER parameters
            ClusteringMethod = options.ClusteringMethod,
            SemanticSimilarityThreshold = options.SemanticSimilarityThreshold,
            TemperatureVariance = options.TemperatureVariance,
            BaseTemperature = options.BaseTemperature,
            UseMultipleProviders = options.UseMultipleProviders,
            RedFlagThreshold = options.RedFlagThreshold
        };
        options = options with
        {
            OnProgress = p =>
            {
                // Progress log
                run.Timeline.Add(new ProgressEntry(p.Phase.ToString(), p.Message, p.Timestamp));
                _logger.LogInformation("[{Project}] {Phase}: {Message}", projectId, p.Phase, p.Message);
                
                // SSE: progress event (for STRATEGIC_PLANNING)
                run.EventChannel.Writer.TryWrite(new SSEEvent
                {
                    Type = "progress",
                    TaskId = p.TaskId,
                    Phase = p.Phase.ToString(),
                    Message = p.Message,
                    Depth = p.Depth,
                    Timestamp = p.Timestamp
                });
                
                // SSE: voting event (for CONSENSUS_PROTOCOL)
                if (p.Voting != null)
                {
                    // Check if consensus reached (either by gap or by message)
                    var gap = p.Voting.LeaderVotes - p.Voting.RunnerUpVotes;
                    var consensusReached = gap >= p.Voting.VotesNeeded || 
                                          (p.Message?.Contains("consensus reached", StringComparison.OrdinalIgnoreCase) ?? false);
                    
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "voting",
                        TaskId = p.TaskId,
                        VotingType = p.Voting.Type.ToString(),
                        Round = p.Voting.Round,
                        TotalVotes = p.Voting.TotalVotes,
                        VotesNeeded = p.Voting.VotesNeeded,
                        LeaderVotes = p.Voting.LeaderVotes,
                        RunnerUpVotes = p.Voting.RunnerUpVotes,
                        ClusterCount = p.Voting.ClusterCount,
                        UsedSemanticClustering = p.Voting.UsedSemanticClustering,
                        Success = consensusReached,
                        Timestamp = p.Timestamp
                    });
                    
                    // Record voting state to file
                    var voteFileName = $"vote_{p.TaskId}_{p.Voting.Type}_R{p.Voting.Round}.md";
                    var consensusStatus = consensusReached ? "✓ CONSENSUS REACHED" : $"⏳ Need {p.Voting.VotesNeeded - gap} more";
                    var voteTypeDesc = p.Voting.Type.ToString() == "Decomposition" 
                        ? "🔀 Deciding HOW to break down this task into sub-steps"
                        : "🎯 Deciding WHAT is the correct solution";
                    
                    var clusteringMode = p.Voting.UsedSemanticClustering 
                        ? "🧠 Semantic Clustering (Embedding-based)" 
                        : "🔢 Hash Matching (Exact)";
                    var voteContent = $"""
                        # 🗳️ Voting Session: {p.Voting.Type}
                        
                        ## Context
                        
                        | Field | Value |
                        |-------|-------|
                        | **Task ID** | `{p.TaskId}` |
                        | **Recursion Depth** | D{p.Depth} |
                        | **Voting Round** | R{p.Voting.Round} |
                        | **Clustering Mode** | {clusteringMode} |
                        | **Cluster Count** | {p.Voting.ClusterCount} |
                        | **Time** | {p.Timestamp:HH:mm:ss.fff} |
                        
                        ## Purpose
                        
                        {voteTypeDesc}
                        
                        **Current Objective:** {p.Message}
                        
                        ## Voting Progress
                        
                        ```
                        Consensus Threshold: K = {p.Voting.VotesNeeded} (leader must lead by K votes)
                        
                        Leader:    {p.Voting.LeaderVotes} votes  {"█".PadRight(Math.Min(20, p.Voting.LeaderVotes), '█')}
                        Runner-up: {p.Voting.RunnerUpVotes} votes  {"█".PadRight(Math.Min(20, p.Voting.RunnerUpVotes), '█')}
                        
                        Current Gap: {gap} / {p.Voting.VotesNeeded} needed
                        Total Samples: {p.Voting.TotalVotes}
                        Clusters: {p.Voting.ClusterCount} (lower = more agreement)
                        ```
                        
                        ## Status
                        
                        **{consensusStatus}**
                        
                        ---
                        *This file is auto-generated during MAKER execution to track voting progress.*
                        """;
                    SaveFile(run, "votes", voteFileName, voteContent);
                    
                    // Generate consensus analysis when consensus is reached
                    if (consensusReached)
                    {
                        GenerateConsensusAnalysis(run, p.TaskId, p.Voting.Type.ToString(), p.Depth);
                    }
                }
                
                // SSE: proposal event (LLM response for SYSTEM_NODES)
                if (p.Proposal != null)
                {
                    var workerId = $"{p.TaskId}:{p.Proposal.ProposalId}";
                    run.WorkerIds.Add(workerId);
                    
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "proposal",
                        TaskId = workerId,
                        Content = p.Proposal.Content,
                        Success = p.Proposal.Success,
                        Message = p.Proposal.Error,
                        Timestamp = p.Timestamp,
                        // Token telemetry
                        PromptTokens = p.Proposal.PromptTokens,
                        CompletionTokens = p.Proposal.CompletionTokens,
                        // LLM provider info
                        ProviderName = p.Proposal.ProviderName
                    });
                    
                    // Track proposal for consensus analysis
                    if (!run.ProposalsByTask.ContainsKey(p.TaskId))
                        run.ProposalsByTask[p.TaskId] = [];
                    
                    run.ProposalsByTask[p.TaskId].Add(new ProposalRecord(
                        p.Proposal.ProposalId,
                        p.Proposal.Content ?? "",
                        p.Proposal.Success,
                        p.Proposal.Error,
                        p.Timestamp,
                        p.Proposal.ProviderName
                    ));
                    
                    // Determine proposal type from ID (D=Decomposition, S=Solution)
                    var proposalType = p.Proposal.ProposalId.StartsWith("D") ? "decomposition" : "solution";
                    var proposalNum = run.ProposalsByTask[p.TaskId].Count;
                    var fileName = $"{proposalType}_{p.TaskId}_{p.Proposal.ProposalId}.md";
                    var providerLabel = string.IsNullOrEmpty(p.Proposal.ProviderName) ? "N/A" : p.Proposal.ProviderName;
                    
                    var content = $"""
                        # {proposalType.ToUpperInvariant()} Proposal #{proposalNum}: {p.Proposal.ProposalId}
                        
                        ## Metadata
                        
                        | Field | Value |
                        |-------|-------|
                        | **Task ID** | `{p.TaskId}` |
                        | **Proposal ID** | `{p.Proposal.ProposalId}` |
                        | **LLM Provider** | 🤖 **{providerLabel}** |
                        | **Recursion Depth** | D{p.Depth} |
                        | **Sequence** | #{proposalNum} for this task |
                        | **Status** | {(p.Proposal.Success ? "✓ Success" : "✗ Failed")} |
                        | **Time** | {p.Timestamp:HH:mm:ss.fff} |
                        
                        ## LLM Response
                        
                        ```
                        {p.Proposal.Content ?? p.Proposal.Error ?? "No content"}
                        ```
                        
                        ---
                        *Generated by {providerLabel} - Raw LLM output captured for consensus analysis.*
                        """;
                    SaveFile(run, "proposals", fileName, content);
                }
                
                // SSE: streaming token event (for real-time LLM output in SYSTEM_NODES)
                if (p.StreamingToken != null)
                {
                    var st = p.StreamingToken;
                    var nodeId = $"{p.TaskId}:{st.ProposalId}";
                    
                    // Log streaming events at Trace level (too verbose for normal viewing)
                    _logger.LogTrace("[SSE-STREAMING] token #{Index} from {WorkerId} ({Provider}), first={First}, last={Last}, contentLen={Len}",
                        st.TokenIndex, st.WorkerId, st.ProviderName, st.IsFirstToken, st.IsLastToken, st.AccumulatedContent?.Length ?? 0);
                    
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "streaming",
                        TaskId = nodeId,
                        WorkerId = st.WorkerId,
                        ProposalId = st.ProposalId,
                        Token = st.Token,
                        AccumulatedContent = st.AccumulatedContent,
                        TokenIndex = st.TokenIndex,
                        IsFirstToken = st.IsFirstToken,
                        IsLastToken = st.IsLastToken,
                        ProviderName = st.ProviderName,
                        // Chat context for SYSTEM_NODES display
                        SystemPrompt = st.SystemPrompt,
                        UserPrompt = st.UserPrompt,
                        Timestamp = p.Timestamp
                    });
                    
                    // When streaming completes, also send a proposal event to create chat card
                    // This ensures Coordinator LLM calls (composition, assessment) also appear as cards
                    if (st.IsLastToken && !string.IsNullOrEmpty(st.AccumulatedContent))
                    {
                        run.EventChannel.Writer.TryWrite(new SSEEvent
                        {
                            Type = "proposal",
                            TaskId = nodeId,
                            Content = st.AccumulatedContent,
                            Success = true,
                            ProviderName = st.ProviderName,
                            // Forward prompts for card display
                            SystemPrompt = st.SystemPrompt,
                            UserPrompt = st.UserPrompt,
                            Timestamp = p.Timestamp
                        });
                        
                        _logger.LogInformation("[CHAT-CARD] Created card for streaming completion: {NodeId} ({Provider})",
                            nodeId, st.ProviderName);
                    }
                }
            }
        };

        // Run in background - DON'T use the HTTP request's cancellation token!
        // The HTTP request completes immediately, but the MAKER execution continues.
        _ = Task.Run(async () =>
        {
            try
            {
                run.Result = await _executor.ExecuteAsync(taskDescription, options, CancellationToken.None);
                run.Status = run.Result.Success ? "completed" : "failed";
                
                _logger.LogInformation("[{Project}] Execution finished. Success={Success}, ContentLength={Length}", 
                    projectId, run.Result.Success, run.Result.Content?.Length ?? 0);

                // ============================================================
                //  Export unified ExecutionTrace bundle (framework-level)
                //
                //  - Enabled when AEVATAR_TRACE_DIR is set (FileExecutionTraceStore).
                //  - Always best-effort: never fail the run because trace export fails.
                // ============================================================
                try
                {
                    var trace = run.Result.ToExecutionTrace();
                    trace.Labels["maker_system.project_id"] = projectId;
                    trace.Labels["maker_system.project_name"] = project.Name;
                    trace.Labels["maker_system.run_id"] = run.RunId;
                    trace.Labels["maker_system.output_dir"] = run.OutputDir ?? string.Empty;

                    await _traceStore.SaveAsync(trace, CancellationToken.None);

                    // Also attach JSON trace to this run's output artifacts for quick UI access.
                    SaveFile(run, "artifacts", "trace.json", trace.ToJsonString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to export ExecutionTrace bundle for project {ProjectId}", projectId);
                }
                
                // Record final result as artifact
                if (!string.IsNullOrEmpty(run.Result.Content))
                {
                    // Generate execution summary
                    var summaryContent = $"""
                        # 📊 Execution Summary
                        
                        ## Overview
                        
                        | Metric | Value |
                        |--------|-------|
                        | **Project** | {project.Name} |
                        | **Run ID** | `{run.RunId}` |
                        | **Status** | {(run.Result.Success ? "✓ Success" : "✗ Failed")} |
                        | **Duration** | {run.Stopwatch.Elapsed.TotalSeconds:F1}s |
                        | **Total LLM Calls** | {run.Result.TotalLLMCalls} |
                        | **Output Directory** | `{run.OutputDir}` |
                        
                        ## Files Generated
                        
                        - **Proposals:** {run.Files["proposals"].Count} files
                        - **Votes:** {run.Files["votes"].Count} files
                        - **Consensus Analysis:** {run.Files["consensus"].Count} files
                        - **Artifacts:** {run.Files["artifacts"].Count + 2} files
                        
                        ## Task Tree
                        
                        Total tasks tracked: {run.ProposalsByTask.Count}
                        
                        ---
                        *Generated by MAKER System*
                        """;
                    SaveFile(run, "artifacts", "00_summary.md", summaryContent);
                    
                    var finalContent = $"""
                        # 📄 Final Report
                        
                        **Project:** {project.Name}  
                        **Run ID:** {run.RunId}  
                        **Status:** {(run.Result.Success ? "✓ Success" : "✗ Failed")}  
                        **Duration:** {run.Stopwatch.Elapsed.TotalSeconds:F1}s  
                        **LLM Calls:** {run.Result.TotalLLMCalls}
                        
                        ## 📊 Token Usage
                        | Metric | Value |
                        |--------|-------|
                        | Prompt Tokens | {run.Result.PromptTokens:N0} |
                        | Completion Tokens | {run.Result.CompletionTokens:N0} |
                        | **Total Tokens** | **{run.Result.TotalTokens:N0}** |
                        
                        ---
                        
                        {run.Result.Content}
                        """;
                    SaveFile(run, "artifacts", "final_report.md", finalContent);
                }
                
                // Record execution trace
                if (run.Result.Trace.RootTask != null)
                {
                    RecordTaskTree(run, run.Result.Trace.RootTask, 0);
                }
                
                run.Timeline.Add(new ProgressEntry(
                    run.Status,
                    run.Result.Success ? "执行完成" : $"执行失败: {run.Result.Error}",
                    DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                run.Status = "failed";
                run.Timeline.Add(new ProgressEntry("Failed", ex.Message, DateTimeOffset.UtcNow));
                _logger.LogError(ex, "Run failed for {Project}", projectId);
                
                // Send error event
                run.EventChannel.Writer.TryWrite(new SSEEvent
                {
                    Type = "error",
                    TaskId = run.RunId,
                    Message = ex.Message,
                    Success = false
                });
            }
            finally
            {
                run.Stopwatch.Stop();
                
                // Send final result event with token statistics
                if (run.Result != null)
                {
                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "result",
                        TaskId = run.RunId,
                        Content = run.Result.Content,
                        Success = run.Result.Success,
                        Message = run.Result.Error,
                        TotalTokens = run.Result.TotalTokens,
                        PromptTokens = (int)run.Result.PromptTokens,
                        CompletionTokens = (int)run.Result.CompletionTokens,
                        TotalLlmCalls = run.Result.TotalLLMCalls
                    });
                }
                
                // Complete the channel
                run.EventChannel.Writer.Complete();
            }
        }, CancellationToken.None);

        return new { success = true, runId = run.RunId };
    }
}

