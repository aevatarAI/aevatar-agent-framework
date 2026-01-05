using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Execution;
using Aevatar.Agents.CreativeReasoning.Messages;
using Microsoft.Extensions.Logging;

namespace CreativeSystem.Infrastructure;

// ============================================================
//  Creative Project Service
//  Manages C-UoT, E-UoT, T-UoT creative reasoning with real-time events
// ============================================================

public record CreativeSolveRequest(
    string Problem,
    string? DomainHint = null,
    string Mode = "Combinational",  // Combinational, Exploratory, Transformative
    int MaxAnalogies = 5,
    int MaxCandidates = 10,
    float FeasibilityThreshold = 0.6f,
    float UtilityWeight = 0.5f,
    float NoveltyWeight = 0.5f,
    // E-UoT options
    int MaxOutsideThoughts = 10,
    int ExplorationDirections = 3,
    // T-UoT options
    int MaxRuleSets = 3,
    int MutationsPerSet = 3,
    float MinRadicality = 0.5f);

public record SampleProblem(
    string Id,
    string Title,
    string Problem,
    string DomainHint,
    string Category,
    string Mode = "Combinational");

public record RunStatus(
    string RunId,
    string Status,
    string Phase,
    string Mode,
    float Progress,
    int AnalogiesFound,
    int ThoughtsExtracted,
    int CandidatesGenerated,
    int CandidatesPassed,
    int LlmCalls,
    long TotalTokens,
    string? Error,
    // T-UoT specific
    int? RulesExposed = null,
    int? HiddenAssumptionsFound = null,
    int? RuleSetsExplored = null);

public record RunResult(
    string RunId,
    bool Success,
    CandidateSolution? BestSolution,
    IReadOnlyList<CandidateSolution> AllCandidates,
    UoTResultTrace Trace,
    // T-UoT specific
    TUoTResultTrace? TUoTTrace = null,
    IReadOnlyList<string>? HiddenAssumptions = null);

public record SSEEvent
{
    public required string Type { get; init; }  // "progress", "analogy", "thought", "candidate", "result", "error"
    public required string RunId { get; init; }
    public string? Phase { get; init; }
    public string? Mode { get; init; }
    public string? Message { get; init; }
    public float? Progress { get; init; }
    public int? AnalogiesFound { get; init; }
    public int? ThoughtsExtracted { get; init; }
    public int? CandidatesGenerated { get; init; }
    public int? CandidatesPassed { get; init; }
    // T-UoT specific
    public int? RulesExposed { get; init; }
    public int? HiddenAssumptionsFound { get; init; }
    public object? Data { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class CreativeRun
{
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string Mode { get; set; } = "Combinational";
    public string Status { get; set; } = "running";
    public string Phase { get; set; } = "Starting";
    public float Progress { get; set; }
    public int AnalogiesFound { get; set; }
    public int ThoughtsExtracted { get; set; }
    public int CandidatesGenerated { get; set; }
    public int CandidatesPassed { get; set; }
    public int LlmCalls { get; set; }
    public long TotalTokens { get; set; }
    public string? Error { get; set; }
    // T-UoT specific
    public int RulesExposed { get; set; }
    public int HiddenAssumptionsFound { get; set; }
    public int RuleSetsExplored { get; set; }
    public IReadOnlyList<string>? HiddenAssumptions { get; set; }
    
    public UoTResult? Result { get; set; }
    public TUoTResult? TUoTResult { get; set; }
    public Channel<SSEEvent> EventChannel { get; } = Channel.CreateUnbounded<SSEEvent>();
}

public class CreativeProjectService
{
    private readonly IUoTExecutor _executor;
    private readonly ILogger<CreativeProjectService> _logger;
    private readonly IExecutionTraceStore _traceStore;
    private readonly ConcurrentDictionary<string, CreativeRun> _runs = new();

    private static readonly List<SampleProblem> SampleProblems =
    [
        // C-UoT: Combinational
        new("bridge", "Bridge Traffic Problem",
            "Design a mechanism for managing two-way traffic on a single-lane bridge where vehicles from both directions need to cross safely without collision.",
            "transportation, distributed systems, resource scheduling",
            "Engineering", "Combinational"),
        
        new("bookstore", "Bookstore Revival",
            "How can a traditional physical bookstore regain growth and relevance in the era of e-commerce and digital books?",
            "retail, business strategy, community",
            "Business", "Combinational"),
        
        // E-UoT: Exploratory
        new("fitness-explore", "Senior Fitness App (Exploratory)",
            "Design an innovative fitness app feature specifically for users aged 60+ that encourages regular physical activity while being safe and engaging. Look for inspiration from unexpected domains.",
            "health, gamification, accessibility, unexpected domains",
            "Product", "Exploratory"),
        
        new("startup-explore", "Innovative Drink Startup (Exploratory)",
            "Develop a unique beverage product concept that addresses an unmet consumer need. Explore concepts from fields beyond traditional food science.",
            "food, consumer goods, innovation, biotechnology",
            "Business", "Exploratory"),
        
        // T-UoT: Transformative
        new("bookstore-transform", "Bookstore Transformation",
            "Challenge hidden assumptions about what a bookstore must be. How can we fundamentally reimagine the concept of a bookstore for the digital age?",
            "retail, business strategy, community, digital transformation",
            "Business", "Transformative"),
        
        new("education-transform", "Education Transformation",
            "Challenge hidden assumptions about education. What if learning didn't require classrooms, grades, or even teachers in the traditional sense?",
            "education, technology, cognitive science",
            "Education", "Transformative"),
        
        new("healthcare-transform", "Healthcare Transformation",
            "Challenge the hidden assumptions about healthcare delivery. What if health wasn't about treating illness but something fundamentally different?",
            "healthcare, prevention, technology, community health",
            "Healthcare", "Transformative")
    ];

    public CreativeProjectService(
        IUoTExecutor executor,
        ILogger<CreativeProjectService> logger,
        IExecutionTraceStore traceStore)
    {
        _executor = executor;
        _logger = logger;
        _traceStore = traceStore;
    }

    public IReadOnlyList<SampleProblem> GetSampleProblems() => SampleProblems;

    public async Task<object> StartSolveAsync(CreativeSolveRequest request, CancellationToken ct)
    {
        var run = new CreativeRun { Mode = request.Mode };
        _runs[run.RunId] = run;

        _logger.LogInformation("Starting {Mode}-UoT run {RunId} for problem: {Problem}",
            request.Mode, run.RunId, request.Problem[..Math.Min(50, request.Problem.Length)]);

        // Start execution in background
        _ = ExecuteAsync(run, request, ct);

        return new { runId = run.RunId, status = "started", mode = request.Mode };
    }

    private async Task ExecuteAsync(CreativeRun run, CreativeSolveRequest request, CancellationToken ct)
    {
        try
        {
            // Build UoT options with progress callback
            var options = new UoTOptions
            {
                Mode = ParseMode(request.Mode),
                ProviderName = AevatarAgentsConstants.DefaultProviderName,
                DomainHint = request.DomainHint,
                MaxAnalogies = request.MaxAnalogies,
                MaxCandidates = request.MaxCandidates,
                FeasibilityThreshold = request.FeasibilityThreshold,
                UtilityWeight = request.UtilityWeight,
                NoveltyWeight = request.NoveltyWeight,
                // E-UoT specific
                MaxOutsideThoughts = request.MaxOutsideThoughts,
                ExplorationDirections = request.ExplorationDirections,
                // T-UoT specific
                MaxRuleSets = request.MaxRuleSets,
                MutationsPerSet = request.MutationsPerSet,
                MinRadicality = request.MinRadicality,
                OnProgress = progress =>
                {
                    run.Phase = progress.Phase.ToString();
                    run.Progress = progress.ProgressPercent;
                    run.AnalogiesFound = progress.AnalogiesFound > 0 ? progress.AnalogiesFound : run.AnalogiesFound;
                    run.ThoughtsExtracted = progress.ThoughtsExtracted > 0 ? progress.ThoughtsExtracted : run.ThoughtsExtracted;
                    run.CandidatesGenerated = progress.CandidatesGenerated > 0 ? progress.CandidatesGenerated : run.CandidatesGenerated;
                    run.CandidatesPassed = progress.CandidatesPassed > 0 ? progress.CandidatesPassed : run.CandidatesPassed;

                    run.EventChannel.Writer.TryWrite(new SSEEvent
                    {
                        Type = "progress",
                        RunId = run.RunId,
                        Mode = run.Mode,
                        Phase = progress.Phase.ToString(),
                        Message = progress.Message,
                        Progress = progress.ProgressPercent,
                        AnalogiesFound = run.AnalogiesFound,
                        ThoughtsExtracted = run.ThoughtsExtracted,
                        CandidatesGenerated = run.CandidatesGenerated,
                        CandidatesPassed = run.CandidatesPassed,
                        RulesExposed = run.RulesExposed,
                        HiddenAssumptionsFound = run.HiddenAssumptionsFound
                    });
                }
            };

            // Execute based on mode
            if (request.Mode == "Transformative")
            {
                await ExecuteTransformativeAsync(run, request.Problem, options, ct);
            }
            else
            {
                await ExecuteStandardAsync(run, request.Problem, options, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Mode}-UoT run {RunId} failed with exception", run.Mode, run.RunId);
            run.Status = "failed";
            run.Error = ex.Message;

            run.EventChannel.Writer.TryWrite(new SSEEvent
            {
                Type = "error",
                RunId = run.RunId,
                Mode = run.Mode,
                Phase = "Failed",
                Message = ex.Message
            });

            run.EventChannel.Writer.Complete();
        }
    }

    private async Task ExecuteStandardAsync(CreativeRun run, string problem, UoTOptions options, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(problem, options, ct);

        run.Result = result;
        run.Status = result.Success ? "completed" : "failed";
        run.LlmCalls = result.Trace.TotalLLMCalls;
        run.TotalTokens = result.Trace.TotalTokens;

        if (!result.Success)
            run.Error = result.Error;

        // ============================================================
        //  Export unified ExecutionTrace bundle (framework-level)
        //
        //  - Enabled when AEVATAR_TRACE_DIR is set (FileExecutionTraceStore).
        //  - Always best-effort: never fail the run because trace export fails.
        // ============================================================
        try
        {
            var trace = result.ToExecutionTrace();
            trace.Labels["creative_system.run_id"] = run.RunId;
            trace.Labels["creative_system.mode"] = run.Mode;
            trace.Labels["creative_system.status"] = run.Status;
            await _traceStore.SaveAsync(trace, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export ExecutionTrace bundle for run {RunId}", run.RunId);
        }

        run.EventChannel.Writer.TryWrite(new SSEEvent
        {
            Type = result.Success ? "result" : "error",
            RunId = run.RunId,
            Mode = run.Mode,
            Phase = "Completed",
            Message = result.Success
                ? $"{run.Mode} reasoning complete! Found {result.AllCandidates.Count} solutions."
                : $"Failed: {result.Error}",
            Progress = 1.0f,
            Data = result.Success ? new
            {
                bestScore = result.BestSolution?.Score?.Composite,
                candidateCount = result.AllCandidates.Count
            } : null
        });

        run.EventChannel.Writer.Complete();

        _logger.LogInformation("{Mode}-UoT run {RunId} completed: Success={Success}, Candidates={Count}",
            run.Mode, run.RunId, result.Success, result.AllCandidates.Count);
    }

    private async Task ExecuteTransformativeAsync(CreativeRun run, string problem, UoTOptions options, CancellationToken ct)
    {
        var result = await _executor.ExecuteTransformativeAsync(problem, options, ct);

        run.TUoTResult = result;
        run.Status = result.Success ? "completed" : "failed";
        run.LlmCalls = result.Trace.TotalLLMCalls;
        run.TotalTokens = result.Trace.TotalTokens;
        run.RulesExposed = result.Trace.RulesExposed;
        run.HiddenAssumptionsFound = result.Trace.HiddenAssumptionsFound;
        run.RuleSetsExplored = result.Trace.RuleSetsExplored;
        run.HiddenAssumptions = result.Trace.HiddenAssumptions.Select(r => r.Content).ToList();

        if (!result.Success)
            run.Error = result.Error;

        // ============================================================
        //  Export unified ExecutionTrace bundle (framework-level)
        // ============================================================
        try
        {
            var trace = result.ToExecutionTrace();
            trace.Labels["creative_system.run_id"] = run.RunId;
            trace.Labels["creative_system.mode"] = run.Mode;
            trace.Labels["creative_system.status"] = run.Status;
            await _traceStore.SaveAsync(trace, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export ExecutionTrace bundle for run {RunId}", run.RunId);
        }

        run.EventChannel.Writer.TryWrite(new SSEEvent
        {
            Type = result.Success ? "result" : "error",
            RunId = run.RunId,
            Mode = run.Mode,
            Phase = "Completed",
            Message = result.Success
                ? $"Transformative reasoning complete! Challenged {result.Trace.HiddenAssumptionsFound} hidden assumptions."
                : $"Failed: {result.Error}",
            Progress = 1.0f,
            RulesExposed = result.Trace.RulesExposed,
            HiddenAssumptionsFound = result.Trace.HiddenAssumptionsFound,
            Data = result.Success ? new
            {
                bestScore = result.BestSolution?.Score?.Composite,
                solutionCount = result.AllSolutions.Count,
                hiddenAssumptions = run.HiddenAssumptions
            } : null
        });

        run.EventChannel.Writer.Complete();

        _logger.LogInformation("T-UoT run {RunId} completed: Success={Success}, Solutions={Count}, Hidden Assumptions={Assumptions}",
            run.RunId, result.Success, result.AllSolutions.Count, result.Trace.HiddenAssumptionsFound);
    }

    public RunStatus GetStatus(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            return new RunStatus(runId, "not_found", "", "Unknown", 0, 0, 0, 0, 0, 0, 0, "Run not found");
        }

        return new RunStatus(
            run.RunId,
            run.Status,
            run.Phase,
            run.Mode,
            run.Progress,
            run.AnalogiesFound,
            run.ThoughtsExtracted,
            run.CandidatesGenerated,
            run.CandidatesPassed,
            run.LlmCalls,
            run.TotalTokens,
            run.Error,
            run.RulesExposed,
            run.HiddenAssumptionsFound,
            run.RuleSetsExplored);
    }

    public RunResult? GetResult(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
            return null;

        // T-UoT result
        if (run.TUoTResult != null)
        {
            // Convert T-UoT solutions to CandidateSolution format
            var candidates = run.TUoTResult.AllSolutions
                .Select(s => new CandidateSolution
                {
                    Id = s.Id,
                    Content = s.Content,
                    Score = s.Score
                })
                .ToList();

            return new RunResult(
                run.RunId,
                run.TUoTResult.Success,
                run.TUoTResult.BestSolution != null ? new CandidateSolution
                {
                    Id = run.TUoTResult.BestSolution.Id,
                    Content = run.TUoTResult.BestSolution.Content,
                    Score = run.TUoTResult.BestSolution.Score
                } : null,
                candidates,
                new UoTResultTrace
                {
                    ExecutionId = run.TUoTResult.Trace.ExecutionId,
                    OriginalProblem = run.TUoTResult.Trace.OriginalProblem,
                    TotalLLMCalls = run.TUoTResult.Trace.TotalLLMCalls,
                    TotalTokens = run.TUoTResult.Trace.TotalTokens,
                    Duration = run.TUoTResult.Trace.Duration
                },
                run.TUoTResult.Trace,
                run.HiddenAssumptions);
        }

        // C-UoT or E-UoT result
        if (run.Result == null)
            return null;

        return new RunResult(
            run.RunId,
            run.Result.Success,
            run.Result.BestSolution,
            run.Result.AllCandidates,
            run.Result.Trace);
    }

    public async IAsyncEnumerable<SSEEvent> GetEventStreamAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            yield return new SSEEvent
            {
                Type = "error",
                RunId = runId,
                Message = "Run not found"
            };
            yield break;
        }

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    public async Task<string?> GetExecutionTraceJsonAsync(string runId, CancellationToken ct = default)
    {
        if (!_runs.TryGetValue(runId, out var run))
            return null;

        var executionId =
            run.TUoTResult?.Trace.ExecutionId ??
            run.Result?.Trace.ExecutionId ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(executionId))
            return null;

        // Prefer stored trace bundle if available.
        try
        {
            var stored = await _traceStore.LoadAsync(executionId, ct);
            if (stored != null)
            {
                return stored.ToJsonString();
            }
        }
        catch
        {
            // Ignore and fall back to in-memory reconstruction.
        }

        if (run.TUoTResult != null)
        {
            return run.TUoTResult.ToExecutionTrace().ToJsonString();
        }

        if (run.Result != null)
        {
            return run.Result.ToExecutionTrace().ToJsonString();
        }

        return null;
    }

    private static Aevatar.Agents.CreativeReasoning.Core.UoTMode ParseMode(string mode) => mode switch
    {
        "Exploratory" => Aevatar.Agents.CreativeReasoning.Core.UoTMode.Exploratory,
        "Transformative" => Aevatar.Agents.CreativeReasoning.Core.UoTMode.Transformative,
        _ => Aevatar.Agents.CreativeReasoning.Core.UoTMode.Combinational
    };
}
