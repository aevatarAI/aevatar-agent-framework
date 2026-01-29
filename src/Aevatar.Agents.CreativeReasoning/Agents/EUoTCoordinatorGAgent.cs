using System.Diagnostics;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Messages;
using Aevatar.Agents.CreativeReasoning.Strategies;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.CreativeReasoning.Agents;

// ============================================================
//  E-UoT (Exploratory UoT) Coordinator Agent
//  Extends C-UoT with Step E: Exploratory Idea Expansion
//  
//  E-UoT Flow:
//  1. Analogical Retrieval & Solution Harvesting
//  2. Decompose Each Solution into Thoughts
//  E. Exploratory Idea Expansion (NEW!)
//  3. Choose Host & Substitution Sites
//  4. Far-then-Analogical Donor Selection (includes outside thoughts)
//  5. Substitute to Synthesize New Combinations
//  6. Evaluate and Rank
// ============================================================

/// <summary>
/// Coordinator Agent for E-UoT exploratory creative reasoning.
/// Discovers novel "outside thoughts" beyond existing solution space.
/// </summary>
public class EUoTCoordinatorGAgent : AIGAgentBase<EUoTCoordinatorState, EUoTCoordinatorConfig>
{
    // ============================================================
    //  Strategies (inherited from C-UoT + exploratory)
    // ============================================================
    
    private IAnalogyStrategy _analogyStrategy = new DefaultAnalogyStrategy();
    private IThoughtDecompositionStrategy _decompositionStrategy = new DefaultThoughtDecompositionStrategy();
    private IExploratoryStrategy _exploratoryStrategy = new DefaultExploratoryStrategy();
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
    private readonly List<ThoughtSubstitution> _selectedSubstitutions = [];

    public EUoTCoordinatorGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"E-UoT Coordinator [{CustomState.ExecutionId}] - Phase: {CustomState.Phase}");
    }

    #region Public API

    public void SetStrategies(
        IAnalogyStrategy? analogyStrategy = null,
        IThoughtDecompositionStrategy? decompositionStrategy = null,
        IExploratoryStrategy? exploratoryStrategy = null,
        IHostSelectionStrategy? hostSelectionStrategy = null,
        IDonorSelectionStrategy? donorSelectionStrategy = null,
        ISynthesisStrategy? synthesisStrategy = null,
        IEvaluationStrategy? evaluationStrategy = null,
        Action<UoTProgress>? progressCallback = null)
    {
        _analogyStrategy = analogyStrategy ?? _analogyStrategy;
        _decompositionStrategy = decompositionStrategy ?? _decompositionStrategy;
        _exploratoryStrategy = exploratoryStrategy ?? _exploratoryStrategy;
        _hostSelectionStrategy = hostSelectionStrategy ?? _hostSelectionStrategy;
        _donorSelectionStrategy = donorSelectionStrategy ?? _donorSelectionStrategy;
        _synthesisStrategy = synthesisStrategy ?? _synthesisStrategy;
        _evaluationStrategy = evaluationStrategy ?? _evaluationStrategy;
        _progressCallback = progressCallback;
    }

    public UoTResult GetResult() => _cachedResult ?? CreateEmptyResult();
    public EUoTExecutionPhase GetPhase() => CustomState.Phase;

    #endregion

    #region Event Handlers

    [EventHandler]
    public async Task HandleStartEUoTRequest(StartEUoTRequest request)
    {
        Logger.LogInformation("E-UoT Coordinator {Id} starting exploratory reasoning for: {Problem}",
            Id, request.Problem[..Math.Min(50, request.Problem.Length)]);

        try
        {
            await InitializeExecutionAsync(request);
            
            // E-UoT flow: C-UoT steps 1-2, then Step E, then 3-6
            await ExecuteStep1_RetrieveAnalogiesAsync(request);
            await ExecuteStep2_DecomposeThoughtsAsync();
            await ExecuteStepE_ExploratoryExpansionAsync();  // NEW!
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

    #region C-UoT Steps (1-2)

    private async Task ExecuteStep1_RetrieveAnalogiesAsync(StartEUoTRequest request)
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseRetrievingAnalogies;
        ReportProgress(UoTPhase.RetrievingAnalogies, "Finding analogous problems...", 0.1f);

        var analogyPrompt = _analogyStrategy.BuildAnalogyRetrievalPrompt(
            request.Problem, request.DomainHint, CustomConfig.MaxAnalogies);

        var analogyResponse = await ChatAsync(ChatRequest.Create(analogyPrompt));
        CustomState.TotalLlmCalls++;
        
        var parsedAnalogies = _analogyStrategy.ParseAnalogies(analogyResponse.Content);

        foreach (var analogy in parsedAnalogies)
        {
            var solutionPrompt = _analogyStrategy.BuildSolutionHarvestPrompt(
                analogy.Description, analogy.Domain, CustomConfig.SolutionsPerAnalogy);

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
                    Id = sol.Id, Content = sol.Content,
                    ProblemId = sol.ProblemId, Effectiveness = sol.EstimatedEffectiveness
                });
            }

            CustomState.Analogies.Add(analogousProblem);
        }

        ReportProgress(UoTPhase.RetrievingAnalogies, 
            $"Found {CustomState.Analogies.Count} analogies", 0.2f,
            analogiesFound: CustomState.Analogies.Count);
    }

    private async Task ExecuteStep2_DecomposeThoughtsAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseDecomposingThoughts;
        ReportProgress(UoTPhase.DecomposingThoughts, "Decomposing into atomic thoughts...", 0.25f);

        foreach (var analogy in CustomState.Analogies)
        {
            foreach (var solution in analogy.Solutions)
            {
                var prompt = _decompositionStrategy.BuildDecompositionPrompt(
                    solution.Content, analogy.Description);

                var response = await ChatAsync(ChatRequest.Create(prompt));
                CustomState.TotalLlmCalls++;

                var thoughts = _decompositionStrategy.ParseThoughts(
                    response.Content, solution.Id, analogy.Id);

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
                    solution.Thoughts.Add(unit);
                    CustomState.AllThoughts.Add(unit);
                }
            }
        }

        ReportProgress(UoTPhase.DecomposingThoughts,
            $"Extracted {CustomState.AllThoughts.Count} thoughts", 0.35f,
            thoughtsExtracted: CustomState.AllThoughts.Count);
    }

    #endregion

    #region Step E: Exploratory Idea Expansion (E-UoT CORE)

    /// <summary>
    /// Step E: Exploratory Idea Expansion
    /// The key innovation of E-UoT: discover outside thoughts beyond existing space.
    /// </summary>
    private async Task ExecuteStepE_ExploratoryExpansionAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseExploringIdeas;
        ReportProgress(UoTPhase.SelectingDonors, "E-UoT: Exploring outside thought space...", 0.4f);

        // Step E.1: Identify exploration directions
        var existingThoughts = CustomState.AllThoughts
            .Select(t => new ParsedThought(t.Id, t.Content, t.Type, t.Position))
            .ToList();

        var directionsPrompt = _exploratoryStrategy.BuildExplorationDirectionsPrompt(
            CustomState.OriginalProblem,
            existingThoughts,
            CustomConfig.ExplorationDirections);

        var directionsResponse = await ChatAsync(ChatRequest.Create(directionsPrompt));
        CustomState.TotalLlmCalls++;

        var directions = _exploratoryStrategy.ParseExplorationDirections(directionsResponse.Content);
        CustomState.ExplorationDirections.AddRange(directions);

        Logger.LogDebug("Identified {Count} exploration directions", directions.Count);

        // Step E.2: Discover outside thoughts in each direction
        var allOutsideThoughts = new List<ParsedOutsideThought>();

        foreach (var direction in directions)
        {
            var discoveryPrompt = _exploratoryStrategy.BuildOutsideThoughtDiscoveryPrompt(
                CustomState.OriginalProblem,
                direction,
                existingThoughts,
                CustomConfig.MaxOutsideThoughts / Math.Max(1, directions.Count));

            var discoveryResponse = await ChatAsync(ChatRequest.Create(discoveryPrompt));
            CustomState.TotalLlmCalls++;

            var discovered = _exploratoryStrategy.ParseOutsideThoughts(discoveryResponse.Content, direction);
            allOutsideThoughts.AddRange(discovered);
        }

        Logger.LogDebug("Discovered {Count} outside thoughts", allOutsideThoughts.Count);

        // Step E.3: Evaluate and filter outside thoughts
        if (allOutsideThoughts.Count > 0)
        {
            var evaluationPrompt = _exploratoryStrategy.BuildOutsideThoughtEvaluationPrompt(
                allOutsideThoughts,
                CustomState.OriginalProblem,
                existingThoughts);

            var evaluationResponse = await ChatAsync(ChatRequest.Create(evaluationPrompt));
            CustomState.TotalLlmCalls++;

            var evaluated = _exploratoryStrategy.ParseEvaluatedOutsideThoughts(evaluationResponse.Content);

            // Add filtered outside thoughts to state
            foreach (var thought in evaluated)
            {
                CustomState.OutsideThoughts.Add(new OutsideThought
                {
                    Id = thought.Id,
                    Content = thought.Content,
                    ExplorationSource = thought.ExplorationSource,
                    ExplorationMethod = thought.ExplorationMethod,
                    NoveltyScore = thought.NoveltyScore,
                    RelevanceScore = thought.RelevanceScore
                });
            }
        }

        ReportProgress(UoTPhase.SelectingDonors,
            $"E-UoT: Discovered {CustomState.OutsideThoughts.Count} outside thoughts",
            0.5f);
    }

    #endregion

    #region C-UoT Steps (3-6) with Outside Thoughts

    private async Task ExecuteStep3_SelectHostAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseSelectingHost;
        ReportProgress(UoTPhase.SelectingHost, "Selecting host solution...", 0.55f);

        var solutionsForSelection = CustomState.Analogies
            .SelectMany(a => a.Solutions.Select(s => new SolutionForSelection(
                s.Id, s.Content, a.Description, a.Domain, s.Thoughts.Count)))
            .ToList();

        var prompt = _hostSelectionStrategy.BuildHostSelectionPrompt(
            CustomState.OriginalProblem, solutionsForSelection);

        var response = await ChatAsync(ChatRequest.Create(prompt));
        CustomState.TotalLlmCalls++;

        var result = _hostSelectionStrategy.ParseHostSelection(response.Content);
        CustomState.HostSolutionId = result.HostSolutionId;
        CustomState.SubstitutionSites.AddRange(result.SubstitutionSites);

        ReportProgress(UoTPhase.SelectingHost,
            $"Host: {result.HostSolutionId} ({result.SubstitutionSites.Count} sites)", 0.6f);
    }

    private async Task ExecuteStep4_SelectDonorsAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseSelectingDonors;
        ReportProgress(UoTPhase.SelectingDonors, "Selecting donors (including outside thoughts)...", 0.65f);

        var hostSolution = GetHostSolution();
        if (hostSolution == null) return;

        _selectedSubstitutions.Clear();

        // Combine existing thoughts with outside thoughts for donor pool
        var donorPool = CustomState.AllThoughts.ToList();
        
        // Convert outside thoughts to ThoughtUnits
        foreach (var outside in CustomState.OutsideThoughts)
        {
            donorPool.Add(new ThoughtUnit
            {
                Id = outside.Id,
                Content = outside.Content,
                SourceProblem = outside.ExplorationSource,
                SourceSolution = "outside",
                Type = ThoughtType.Core,  // Outside thoughts tend to be core concepts
                SemanticDistance = 1.0f - outside.RelevanceScore  // High novelty = high distance
            });
        }

        foreach (var sitePosition in CustomState.SubstitutionSites)
        {
            var originalThought = hostSolution.Thoughts
                .FirstOrDefault(t => t.Position == sitePosition);
            if (originalThought == null) continue;

            var candidateDonors = _donorSelectionStrategy.FilterByDistance(
                originalThought, donorPool, CustomConfig.FarDistanceThreshold, CustomConfig.FarDonorsCount);

            if (candidateDonors.Count == 0) continue;

            var prompt = _donorSelectionStrategy.BuildDonorSelectionPrompt(
                originalThought, candidateDonors, CustomState.OriginalProblem, CustomConfig.FarDistanceThreshold);

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

        ReportProgress(UoTPhase.SelectingDonors,
            $"Selected {_selectedSubstitutions.Count} substitutions", 0.7f);
    }

    private async Task ExecuteStep5_SynthesizeSolutionsAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseSynthesizing;
        ReportProgress(UoTPhase.Synthesizing, "Synthesizing creative solutions...", 0.75f);

        var hostSolution = GetHostSolution();
        if (hostSolution == null) return;

        var candidatesGenerated = 0;
        var maxCandidates = Math.Min(CustomConfig.MaxCandidates, _selectedSubstitutions.Count + 1);

        if (_selectedSubstitutions.Count > 0)
        {
            await GenerateCandidateAsync(hostSolution.Content, _selectedSubstitutions);
            candidatesGenerated++;
        }

        foreach (var sub in _selectedSubstitutions.Take(maxCandidates - candidatesGenerated))
        {
            await GenerateCandidateAsync(hostSolution.Content, [sub]);
            candidatesGenerated++;
        }

        ReportProgress(UoTPhase.Synthesizing,
            $"Generated {CustomState.Candidates.Count} candidates", 0.85f,
            candidatesGenerated: CustomState.Candidates.Count);
    }

    private async Task GenerateCandidateAsync(string hostContent, IReadOnlyList<ThoughtSubstitution> substitutions)
    {
        var prompt = _synthesisStrategy.BuildSynthesisPrompt(
            hostContent, substitutions, CustomState.OriginalProblem);

        var response = await ChatAsync(ChatRequest.Create(prompt));
        CustomState.TotalLlmCalls++;

        var synthesized = _synthesisStrategy.ParseSynthesizedSolution(response.Content);

        var candidate = new CandidateSolution
        {
            Id = $"candidate_{CustomState.Candidates.Count + 1}",
            Content = synthesized,
            HostSolutionId = CustomState.HostSolutionId,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        candidate.Substitutions.AddRange(substitutions);
        CustomState.Candidates.Add(candidate);
    }

    private async Task ExecuteStep6_EvaluateCandidatesAsync()
    {
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseEvaluating;
        ReportProgress(UoTPhase.Evaluating, "Evaluating candidates...", 0.9f);

        var existingSolutions = CustomState.Analogies
            .SelectMany(a => a.Solutions)
            .Select(s => s.Content)
            .ToList();

        var passedCandidates = new List<CandidateSolution>();

        foreach (var candidate in CustomState.Candidates)
        {
            // Feasibility
            var feasPrompt = _evaluationStrategy.BuildFeasibilityPrompt(
                candidate.Content, CustomState.OriginalProblem);
            var feasResponse = await ChatAsync(ChatRequest.Create(feasPrompt));
            CustomState.TotalLlmCalls++;
            var feasibility = _evaluationStrategy.ParseFeasibility(feasResponse.Content);
            
            if (feasibility.Score < CustomState.FeasibilityThreshold) continue;

            // Utility
            var utilPrompt = _evaluationStrategy.BuildUtilityPrompt(
                candidate.Content, CustomState.OriginalProblem);
            var utilResponse = await ChatAsync(ChatRequest.Create(utilPrompt));
            CustomState.TotalLlmCalls++;
            var utility = _evaluationStrategy.ParseUtility(utilResponse.Content);

            // Novelty
            var novPrompt = _evaluationStrategy.BuildNoveltyPrompt(
                candidate.Content, CustomState.OriginalProblem, existingSolutions);
            var novResponse = await ChatAsync(ChatRequest.Create(novPrompt));
            CustomState.TotalLlmCalls++;
            var novelty = _evaluationStrategy.ParseNovelty(novResponse.Content);

            var composite = _evaluationStrategy.ComputeCompositeScore(
                utility, novelty, CustomConfig.UtilityWeight, CustomConfig.NoveltyWeight);

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

        var ranked = passedCandidates.OrderByDescending(c => c.Score.Composite).ToList();
        CustomState.Candidates.Clear();
        CustomState.Candidates.AddRange(ranked);

        if (ranked.Count > 0)
            CustomState.BestSolution = ranked[0];

        ReportProgress(UoTPhase.Evaluating,
            $"{passedCandidates.Count} passed feasibility", 0.95f,
            candidatesPassed: passedCandidates.Count);
    }

    #endregion

    #region Helpers

    private async Task InitializeExecutionAsync(StartEUoTRequest request)
    {
        CustomState.ExecutionId = request.ExecutionId;
        CustomState.OriginalProblem = request.Problem;
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseIdle;
        CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        CustomConfig.MaxAnalogies = request.MaxAnalogies > 0 ? request.MaxAnalogies : 5;
        CustomConfig.MaxCandidates = request.MaxCandidates > 0 ? request.MaxCandidates : 10;
        CustomConfig.FeasibilityThreshold = request.FeasibilityThreshold > 0 ? request.FeasibilityThreshold : 0.6f;
        CustomConfig.MaxOutsideThoughts = request.MaxOutsideThoughts > 0 ? request.MaxOutsideThoughts : 10;
        CustomConfig.ExplorationDirections = request.ExplorationDirections > 0 ? request.ExplorationDirections : 3;
        CustomConfig.SolutionsPerAnalogy = 3;
        CustomConfig.FarDistanceThreshold = 0.6f;
        CustomConfig.FarDonorsCount = 5;  // More donors to include outside thoughts
        CustomConfig.UtilityWeight = 0.5f;
        CustomConfig.NoveltyWeight = 0.5f;

        CustomState.FeasibilityThreshold = CustomConfig.FeasibilityThreshold;
        CustomState.MaxAnalogies = CustomConfig.MaxAnalogies;
        CustomState.MaxCandidates = CustomConfig.MaxCandidates;
        CustomState.MaxOutsideThoughts = CustomConfig.MaxOutsideThoughts;

        await InitializeAsync(request.ProviderName, config =>
        {
            config.Temperature = 0.85f;  // Slightly higher for exploration
            config.MaxOutputTokens = 4096;
        });

        _stopwatch.Restart();
        ReportProgress(UoTPhase.Starting, "E-UoT: Exploratory Creative Reasoning initialized", 0.05f);
    }

    private async Task CompleteExecutionAsync(StartEUoTRequest request)
    {
        _stopwatch.Stop();
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseCompleted;
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
                ThoughtsExtracted = CustomState.AllThoughts.Count + CustomState.OutsideThoughts.Count,
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
                ? $"E-UoT complete! Score: {CustomState.BestSolution.Score?.Composite:F2} | Outside thoughts: {CustomState.OutsideThoughts.Count}"
                : "E-UoT complete (no viable solutions)",
            1.0f);

        await PublishAsync(new EUoTCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = _cachedResult.Success,
            BestSolution = CustomState.BestSolution,
            OutsideThoughtsDiscovered = CustomState.OutsideThoughts.Count,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            TotalTokens = CustomState.TotalTokens,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

    private async Task HandleExecutionFailureAsync(StartEUoTRequest request, Exception ex)
    {
        Logger.LogError(ex, "E-UoT Coordinator {Id} failed", Id);
        _stopwatch.Stop();
        CustomState.Phase = EUoTExecutionPhase.EuotPhaseFailed;
        _cachedResult = CreateEmptyResult(ex.Message);

        await PublishAsync(new EUoTCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = false,
            Error = ex.Message,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

    private AnalogousSolution? GetHostSolution() =>
        CustomState.Analogies.SelectMany(a => a.Solutions)
            .FirstOrDefault(s => s.Id == CustomState.HostSolutionId);

    private void ReportProgress(UoTPhase phase, string message, float progress,
        int analogiesFound = 0, int thoughtsExtracted = 0, int candidatesGenerated = 0, int candidatesPassed = 0)
    {
        _progressCallback?.Invoke(new UoTProgress
        {
            Phase = phase, Message = message, ProgressPercent = progress,
            AnalogiesFound = analogiesFound, ThoughtsExtracted = thoughtsExtracted,
            CandidatesGenerated = candidatesGenerated, CandidatesPassed = candidatesPassed
        });
    }

    private UoTResult CreateEmptyResult(string? error = null) => new()
    {
        Success = false, Error = error ?? "Not available",
        Trace = new UoTResultTrace
        {
            ExecutionId = CustomState.ExecutionId ?? "",
            OriginalProblem = CustomState.OriginalProblem ?? "",
            Duration = _stopwatch.Elapsed
        }
    };

    #endregion
}

