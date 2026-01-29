using System.Diagnostics;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Messages;
using Aevatar.Agents.CreativeReasoning.Strategies;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.CreativeReasoning.Agents;

// ============================================================
//  Universe of Thoughts (UoT) Coordinator Agent
//  Implements C-UoT: Combinational Creative Reasoning
//
//  C-UoT Flow:
//  1. Analogical Retrieval & Solution Harvesting
//  2. Decompose Each Solution into Thoughts
//  3. Choose Host & Substitution Sites
//  4. Far-then-Analogical Donor Selection
//  5. Substitute to Synthesize New Combinations
//  6. Evaluate and Rank by Feasibility, Utility, Novelty
// ============================================================

/// <summary>
/// Coordinator Agent for C-UoT creative reasoning.
/// Orchestrates the six-step creative process.
/// </summary>
public class UoTCoordinatorGAgent : AIGAgentBase<UoTCoordinatorState, UoTCoordinatorConfig>
{
    // ============================================================
    //  Strategies (pluggable algorithms)
    // ============================================================
    
    private IAnalogyStrategy _analogyStrategy = new DefaultAnalogyStrategy();
    private IThoughtDecompositionStrategy _decompositionStrategy = new DefaultThoughtDecompositionStrategy();
    private IHostSelectionStrategy _hostSelectionStrategy = new DefaultHostSelectionStrategy();
    private IDonorSelectionStrategy _donorSelectionStrategy = new DefaultDonorSelectionStrategy();
    private ISynthesisStrategy _synthesisStrategy = new DefaultSynthesisStrategy();
    private IEvaluationStrategy _evaluationStrategy = new DefaultEvaluationStrategy();

    // ============================================================
    //  Runtime State
    // ============================================================
    
    private readonly Stopwatch _stopwatch = new();
    private Action<UoTProgress>? _progressCallback;
    private UoTResult? _cachedResult;

    public UoTCoordinatorGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"UoT Coordinator [{CustomState.ExecutionId}] - Phase: {CustomState.Phase}");
    }

    #region Public API

    /// <summary>
    /// Set custom strategies (optional).
    /// </summary>
    public void SetStrategies(
        IAnalogyStrategy? analogyStrategy = null,
        IThoughtDecompositionStrategy? decompositionStrategy = null,
        IHostSelectionStrategy? hostSelectionStrategy = null,
        IDonorSelectionStrategy? donorSelectionStrategy = null,
        ISynthesisStrategy? synthesisStrategy = null,
        IEvaluationStrategy? evaluationStrategy = null,
        Action<UoTProgress>? progressCallback = null)
    {
        _analogyStrategy = analogyStrategy ?? _analogyStrategy;
        _decompositionStrategy = decompositionStrategy ?? _decompositionStrategy;
        _hostSelectionStrategy = hostSelectionStrategy ?? _hostSelectionStrategy;
        _donorSelectionStrategy = donorSelectionStrategy ?? _donorSelectionStrategy;
        _synthesisStrategy = synthesisStrategy ?? _synthesisStrategy;
        _evaluationStrategy = evaluationStrategy ?? _evaluationStrategy;
        _progressCallback = progressCallback;
    }

    /// <summary>
    /// Get execution result after completion.
    /// </summary>
    public UoTResult GetResult() => _cachedResult ?? CreateEmptyResult();

    /// <summary>
    /// Get current execution phase.
    /// </summary>
    public UoTExecutionPhase GetPhase() => CustomState.Phase;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Main entry point - handle creative reasoning request.
    /// </summary>
    [EventHandler]
    public async Task HandleStartCreativeReasoningRequest(StartCreativeReasoningRequest request)
    {
        Logger.LogInformation("UoT Coordinator {Id} starting creative reasoning for: {Problem}",
            Id, request.Problem[..Math.Min(50, request.Problem.Length)]);

        try
        {
            await InitializeExecutionAsync(request);
            
            // Execute C-UoT six-step process
            await ExecuteStep1_RetrieveAnalogiesAsync(request);
            await ExecuteStep2_DecomposeThoughtsAsync();
            await ExecuteStep3_SelectHostAsync();
            await ExecuteStep4_SelectDonorsAsync();
            await ExecuteStep5_SynthesizeSolutionsAsync();
            await ExecuteStep6_EvaluateCandidatesAsync();

            await CompleteExecutionAsync(request);
        }
        catch (Exception ex)
        {
            await HandleExecutionFailureAsync(request, ex);
        }
    }

    #endregion

    #region C-UoT Step Implementations

    /// <summary>
    /// Step 1: Analogical Retrieval &amp; Solution Harvesting
    /// Find similar problems from different domains and collect their solutions.
    /// </summary>
    private async Task ExecuteStep1_RetrieveAnalogiesAsync(StartCreativeReasoningRequest request)
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseRetrievingAnalogies;
        ReportProgress(UoTPhase.RetrievingAnalogies, "Finding analogous problems from diverse domains...", 0.1f);

        // Step 1a: Find analogous problems
        var analogyPrompt = _analogyStrategy.BuildAnalogyRetrievalPrompt(
            request.Problem, 
            request.DomainHint, 
            CustomConfig.MaxAnalogies);

        var analogyResponse = await ChatAsync(ChatRequest.Create(analogyPrompt));
        CustomState.TotalLlmCalls++;
        
        var parsedAnalogies = _analogyStrategy.ParseAnalogies(analogyResponse.Content);
        Logger.LogDebug("Found {Count} analogous problems", parsedAnalogies.Count);

        // Step 1b: Harvest solutions for each analogy
        foreach (var analogy in parsedAnalogies)
        {
            var solutionPrompt = _analogyStrategy.BuildSolutionHarvestPrompt(
                analogy.Description, 
                analogy.Domain, 
                CustomConfig.SolutionsPerAnalogy);

            var solutionResponse = await ChatAsync(ChatRequest.Create(solutionPrompt));
            CustomState.TotalLlmCalls++;

            var parsedSolutions = _analogyStrategy.ParseSolutions(solutionResponse.Content, analogy.Id);

            var analogousProblem = new AnalogousProblem
            {
                Id = analogy.Id,
                Description = analogy.Description,
                Domain = analogy.Domain,
                Similarity = analogy.EstimatedSimilarity
            };

            foreach (var sol in parsedSolutions)
            {
                analogousProblem.Solutions.Add(new AnalogousSolution
                {
                    Id = sol.Id,
                    Content = sol.Content,
                    ProblemId = sol.ProblemId,
                    Effectiveness = sol.EstimatedEffectiveness
                });
            }

            CustomState.Analogies.Add(analogousProblem);
        }

        ReportProgress(UoTPhase.RetrievingAnalogies, 
            $"Found {CustomState.Analogies.Count} analogies with {CustomState.Analogies.Sum(a => a.Solutions.Count)} solutions",
            0.2f,
            analogiesFound: CustomState.Analogies.Count);
    }

    /// <summary>
    /// Step 2: Decompose Each Solution into Thoughts
    /// Break down solutions into atomic, recombinable thought units.
    /// </summary>
    private async Task ExecuteStep2_DecomposeThoughtsAsync()
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseDecomposingThoughts;
        ReportProgress(UoTPhase.DecomposingThoughts, "Decomposing solutions into atomic thoughts...", 0.25f);

        foreach (var analogy in CustomState.Analogies)
        {
            foreach (var solution in analogy.Solutions)
            {
                var prompt = _decompositionStrategy.BuildDecompositionPrompt(
                    solution.Content, 
                    analogy.Description);

                var response = await ChatAsync(ChatRequest.Create(prompt));
                CustomState.TotalLlmCalls++;

                var thoughts = _decompositionStrategy.ParseThoughts(
                    response.Content, 
                    solution.Id, 
                    analogy.Id);

                foreach (var thought in thoughts)
                {
                    var unit = new ThoughtUnit
                    {
                        Id = thought.Id,
                        Content = thought.Content,
                        SourceProblem = analogy.Description,
                        SourceSolution = solution.Id,
                        Type = thought.Type,
                        Position = thought.Position
                    };

                    // Generate embedding for semantic distance calculation
                    if (TryGetEmbeddingGenerator(out var embeddingGenerator))
                    {
                        try
                        {
                            var embedding = await GenerateEmbeddingAsync(thought.Content);
                            if (embedding != null)
                            {
                                unit.Embedding.AddRange(embedding.Vector.ToArray());
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to generate embedding for thought {ThoughtId}", thought.Id);
                        }
                    }

                    solution.Thoughts.Add(unit);
                    CustomState.AllThoughts.Add(unit);
                }
            }
        }

        ReportProgress(UoTPhase.DecomposingThoughts,
            $"Extracted {CustomState.AllThoughts.Count} atomic thoughts",
            0.4f,
            thoughtsExtracted: CustomState.AllThoughts.Count);
    }

    /// <summary>
    /// Step 3: Choose Host &amp; Substitution Sites
    /// Select the best base solution and identify where to substitute thoughts.
    /// </summary>
    private async Task ExecuteStep3_SelectHostAsync()
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseSelectingHost;
        ReportProgress(UoTPhase.SelectingHost, "Selecting optimal host solution...", 0.45f);

        var solutionsForSelection = CustomState.Analogies
            .SelectMany(a => a.Solutions.Select(s => new SolutionForSelection(
                s.Id,
                s.Content,
                a.Description,
                a.Domain,
                s.Thoughts.Count)))
            .ToList();

        var prompt = _hostSelectionStrategy.BuildHostSelectionPrompt(
            CustomState.OriginalProblem, 
            solutionsForSelection);

        var response = await ChatAsync(ChatRequest.Create(prompt));
        CustomState.TotalLlmCalls++;

        var result = _hostSelectionStrategy.ParseHostSelection(response.Content);
        CustomState.HostSolutionId = result.HostSolutionId;
        CustomState.SubstitutionSites.AddRange(result.SubstitutionSites);

        Logger.LogDebug("Selected host: {HostId} with {SiteCount} substitution sites",
            result.HostSolutionId, result.SubstitutionSites.Count);

        ReportProgress(UoTPhase.SelectingHost,
            $"Host selected: {result.HostSolutionId} ({result.SubstitutionSites.Count} substitution sites)",
            0.5f);
    }

    /// <summary>
    /// Step 4: Far-then-Analogical Donor Selection
    /// Select donor thoughts prioritizing semantic distance for novelty.
    /// </summary>
    private async Task ExecuteStep4_SelectDonorsAsync()
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseSelectingDonors;
        ReportProgress(UoTPhase.SelectingDonors, "Selecting donor thoughts (Far-then-Analogical)...", 0.55f);

        var hostSolution = GetHostSolution();
        if (hostSolution == null)
        {
            Logger.LogWarning("Host solution not found: {HostId}", CustomState.HostSolutionId);
            return;
        }

        _selectedSubstitutions.Clear();

        foreach (var sitePosition in CustomState.SubstitutionSites)
        {
            var originalThought = hostSolution.Thoughts
                .FirstOrDefault(t => t.Position == sitePosition);

            if (originalThought == null) continue;

            // Filter candidates using Far-then-Analogical heuristic
            var candidateDonors = _donorSelectionStrategy.FilterByDistance(
                originalThought,
                CustomState.AllThoughts.ToList(),
                CustomConfig.FarDistanceThreshold,
                CustomConfig.FarDonorsCount);

            if (candidateDonors.Count == 0) continue;

            var prompt = _donorSelectionStrategy.BuildDonorSelectionPrompt(
                originalThought,
                candidateDonors,
                CustomState.OriginalProblem,
                CustomConfig.FarDistanceThreshold);

            var response = await ChatAsync(ChatRequest.Create(prompt));
            CustomState.TotalLlmCalls++;

            var result = _donorSelectionStrategy.ParseDonorSelection(response.Content);
            var donorThought = candidateDonors.FirstOrDefault(d => d.Id == result.DonorThoughtId)
                ?? candidateDonors.First();

            _selectedSubstitutions.Add(new ThoughtSubstitution
            {
                Original = originalThought,
                Donor = donorThought,
                SubstitutionSite = sitePosition,
                Rationale = result.Rationale
            });
        }

        Logger.LogDebug("Selected {Count} donor substitutions", _selectedSubstitutions.Count);
        ReportProgress(UoTPhase.SelectingDonors,
            $"Selected {_selectedSubstitutions.Count} donor thoughts",
            0.6f);
    }

    // Temporary storage for substitutions (not in protobuf state for simplicity)
    private readonly List<ThoughtSubstitution> _selectedSubstitutions = [];

    /// <summary>
    /// Step 5: Substitute to Synthesize New Combinations
    /// Create novel candidate solutions from host + donor thoughts.
    /// </summary>
    private async Task ExecuteStep5_SynthesizeSolutionsAsync()
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseSynthesizing;
        ReportProgress(UoTPhase.Synthesizing, "Synthesizing creative solutions...", 0.65f);

        var hostSolution = GetHostSolution();
        if (hostSolution == null) return;

        // Generate multiple candidates with different substitution combinations
        var candidatesGenerated = 0;
        var maxCandidates = Math.Min(CustomConfig.MaxCandidates, _selectedSubstitutions.Count + 1);

        // Full substitution candidate
        if (_selectedSubstitutions.Count > 0)
        {
            await GenerateCandidateAsync(hostSolution.Content, _selectedSubstitutions);
            candidatesGenerated++;
        }

        // Partial substitution variants (one substitution at a time)
        foreach (var sub in _selectedSubstitutions.Take(maxCandidates - candidatesGenerated))
        {
            await GenerateCandidateAsync(hostSolution.Content, [sub]);
            candidatesGenerated++;
        }

        ReportProgress(UoTPhase.Synthesizing,
            $"Generated {CustomState.Candidates.Count} candidate solutions",
            0.75f,
            candidatesGenerated: CustomState.Candidates.Count);
    }

    private async Task GenerateCandidateAsync(string hostContent, IReadOnlyList<ThoughtSubstitution> substitutions)
    {
        var prompt = _synthesisStrategy.BuildSynthesisPrompt(
            hostContent,
            substitutions,
            CustomState.OriginalProblem);

        var response = await ChatAsync(ChatRequest.Create(prompt));
        CustomState.TotalLlmCalls++;

        var synthesizedContent = _synthesisStrategy.ParseSynthesizedSolution(response.Content);

        var candidate = new CandidateSolution
        {
            Id = $"candidate_{CustomState.Candidates.Count + 1}",
            Content = synthesizedContent,
            HostSolutionId = CustomState.HostSolutionId,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        candidate.Substitutions.AddRange(substitutions);

        CustomState.Candidates.Add(candidate);
    }

    /// <summary>
    /// Step 6: Evaluate and Rank by Feasibility, Utility, Novelty
    /// Three-dimensional evaluation with feasibility as hard constraint.
    /// </summary>
    private async Task ExecuteStep6_EvaluateCandidatesAsync()
    {
        CustomState.Phase = UoTExecutionPhase.UotPhaseEvaluating;
        ReportProgress(UoTPhase.Evaluating, "Evaluating candidates (Feasibility → Utility → Novelty)...", 0.8f);

        var existingSolutions = CustomState.Analogies
            .SelectMany(a => a.Solutions)
            .Select(s => s.Content)
            .ToList();

        var passedCandidates = new List<CandidateSolution>();

        foreach (var candidate in CustomState.Candidates)
        {
            // Feasibility (HARD CONSTRAINT)
            var feasibilityPrompt = _evaluationStrategy.BuildFeasibilityPrompt(
                candidate.Content,
                CustomState.OriginalProblem);

            var feasibilityResponse = await ChatAsync(ChatRequest.Create(feasibilityPrompt));
            CustomState.TotalLlmCalls++;

            var feasibility = _evaluationStrategy.ParseFeasibility(feasibilityResponse.Content);
            
            if (feasibility.Score < CustomState.FeasibilityThreshold)
            {
                Logger.LogDebug("Candidate {Id} failed feasibility: {Score} < {Threshold}",
                    candidate.Id, feasibility.Score, CustomState.FeasibilityThreshold);
                continue;
            }

            // Utility
            var utilityPrompt = _evaluationStrategy.BuildUtilityPrompt(
                candidate.Content,
                CustomState.OriginalProblem);

            var utilityResponse = await ChatAsync(ChatRequest.Create(utilityPrompt));
            CustomState.TotalLlmCalls++;

            var utility = _evaluationStrategy.ParseUtility(utilityResponse.Content);

            // Novelty
            var noveltyPrompt = _evaluationStrategy.BuildNoveltyPrompt(
                candidate.Content,
                CustomState.OriginalProblem,
                existingSolutions);

            var noveltyResponse = await ChatAsync(ChatRequest.Create(noveltyPrompt));
            CustomState.TotalLlmCalls++;

            var novelty = _evaluationStrategy.ParseNovelty(noveltyResponse.Content);

            // Composite score
            var composite = _evaluationStrategy.ComputeCompositeScore(
                utility,
                novelty,
                CustomConfig.UtilityWeight,
                CustomConfig.NoveltyWeight);

            candidate.Score = new CreativeScore
            {
                Feasibility = feasibility.Score,
                Utility = utility,
                Novelty = novelty,
                Composite = composite,
                EvaluationRationale = feasibility.Rationale
            };

            passedCandidates.Add(candidate);
        }

        // Rank by composite score (descending)
        var rankedCandidates = passedCandidates
            .OrderByDescending(c => c.Score.Composite)
            .ToList();

        CustomState.Candidates.Clear();
        CustomState.Candidates.AddRange(rankedCandidates);

        if (rankedCandidates.Count > 0)
        {
            CustomState.BestSolution = rankedCandidates[0];
        }

        ReportProgress(UoTPhase.Evaluating,
            $"Evaluated: {passedCandidates.Count}/{CustomState.Candidates.Count} passed feasibility",
            0.95f,
            candidatesPassed: passedCandidates.Count);
    }

    #endregion

    #region Helper Methods

    private async Task InitializeExecutionAsync(StartCreativeReasoningRequest request)
    {
        CustomState.ExecutionId = request.ExecutionId;
        CustomState.OriginalProblem = request.Problem;
        CustomState.Phase = UoTExecutionPhase.UotPhaseIdle;
        CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Apply configuration overrides
        CustomConfig.MaxAnalogies = request.MaxAnalogies > 0 ? request.MaxAnalogies : 5;
        CustomConfig.MaxCandidates = request.MaxCandidates > 0 ? request.MaxCandidates : 10;
        CustomConfig.FeasibilityThreshold = request.FeasibilityThreshold > 0 ? request.FeasibilityThreshold : 0.6f;
        CustomConfig.UtilityWeight = request.UtilityWeight > 0 ? request.UtilityWeight : 0.5f;
        CustomConfig.NoveltyWeight = request.NoveltyWeight > 0 ? request.NoveltyWeight : 0.5f;
        CustomConfig.SolutionsPerAnalogy = 3;
        CustomConfig.FarDistanceThreshold = 0.6f;
        CustomConfig.FarDonorsCount = 3;

        CustomState.FeasibilityThreshold = CustomConfig.FeasibilityThreshold;
        CustomState.MaxAnalogies = CustomConfig.MaxAnalogies;
        CustomState.MaxCandidates = CustomConfig.MaxCandidates;

        await InitializeAsync(request.ProviderName, config =>
        {
            config.Temperature = 0.8f; // Higher temperature for creativity
            config.MaxOutputTokens = 4096;
        });

        _stopwatch.Restart();
        ReportProgress(UoTPhase.Starting, "C-UoT Creative Reasoning System initialized", 0.05f);
    }

    private async Task CompleteExecutionAsync(StartCreativeReasoningRequest request)
    {
        _stopwatch.Stop();
        CustomState.Phase = UoTExecutionPhase.UotPhaseCompleted;
        CustomState.CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        _cachedResult = new UoTResult
        {
            Success = CustomState.BestSolution != null,
            BestSolution = CustomState.BestSolution,
            AllCandidates = CustomState.Candidates.ToList(),
            Trace = new UoTResultTrace
            {
                ExecutionId = CustomState.ExecutionId,
                OriginalProblem = CustomState.OriginalProblem,
                AnalogiesExplored = CustomState.Analogies.Count,
                ThoughtsExtracted = CustomState.AllThoughts.Count,
                CandidatesGenerated = CustomState.Candidates.Count,
                CandidatesPassedFeasibility = CustomState.Candidates.Count(c => c.Score != null),
                TotalLLMCalls = CustomState.TotalLlmCalls,
                TotalTokens = CustomState.TotalTokens,
                Duration = _stopwatch.Elapsed,
                Analogies = CustomState.Analogies.ToList()
            }
        };

        ReportProgress(UoTPhase.Completed,
            CustomState.BestSolution != null
                ? $"Creative reasoning complete! Best score: {CustomState.BestSolution.Score?.Composite:F2}"
                : "Creative reasoning complete (no viable solutions found)",
            1.0f);

        await PublishAsync(new CreativeReasoningCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = _cachedResult.Success,
            BestSolution = CustomState.BestSolution,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            TotalTokens = CustomState.TotalTokens,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

    private async Task HandleExecutionFailureAsync(StartCreativeReasoningRequest request, Exception ex)
    {
        Logger.LogError(ex, "UoT Coordinator {Id} failed during execution", Id);
        _stopwatch.Stop();
        CustomState.Phase = UoTExecutionPhase.UotPhaseFailed;

        _cachedResult = CreateEmptyResult(ex.Message);

        await PublishAsync(new CreativeReasoningCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = false,
            Error = ex.Message,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

    private AnalogousSolution? GetHostSolution()
    {
        return CustomState.Analogies
            .SelectMany(a => a.Solutions)
            .FirstOrDefault(s => s.Id == CustomState.HostSolutionId);
    }

    private void ReportProgress(UoTPhase phase, string message, float progress,
        int analogiesFound = 0, int thoughtsExtracted = 0, int candidatesGenerated = 0, int candidatesPassed = 0)
    {
        var progressInfo = new UoTProgress
        {
            Phase = phase,
            Message = message,
            ProgressPercent = progress,
            AnalogiesFound = analogiesFound,
            ThoughtsExtracted = thoughtsExtracted,
            CandidatesGenerated = candidatesGenerated,
            CandidatesPassed = candidatesPassed
        };

        Logger.LogDebug("[UoT] Phase={Phase}, Progress={Progress:P0}", phase, progress);
        _progressCallback?.Invoke(progressInfo);
    }

    private UoTResult CreateEmptyResult(string? error = null)
    {
        return new UoTResult
        {
            Success = false,
            Error = error ?? "Result not available",
            Trace = new UoTResultTrace
            {
                ExecutionId = CustomState.ExecutionId ?? "",
                OriginalProblem = CustomState.OriginalProblem ?? "",
                Duration = _stopwatch.Elapsed
            }
        };
    }

    #endregion
}

