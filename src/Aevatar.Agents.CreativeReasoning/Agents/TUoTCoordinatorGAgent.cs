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
//  T-UoT (Transformative UoT) Coordinator Agent
//  The most profound form of creative reasoning: changing the rules
//  
//  T-UoT Flow:
//  1. Expose Rules (including hidden assumptions)
//  2. Generate Mutated Rule Sets
//  3. Explore Each New Rule Space for Solutions
//  4. Evaluate Transformative Solutions
//  
//  Philosophy: Hidden assumptions are invisible cages.
//  True innovation requires making them visible, then breaking them.
// ============================================================

/// <summary>
/// Coordinator Agent for T-UoT transformative creative reasoning.
/// Challenges hidden assumptions and explores radically new solution spaces.
/// </summary>
public class TUoTCoordinatorGAgent : AIGAgentBase<TUoTCoordinatorState, TUoTCoordinatorConfig>
{
    // ============================================================
    //  Strategies
    // ============================================================
    
    private IRuleMutationStrategy _ruleMutationStrategy = new DefaultRuleMutationStrategy();
    private IEvaluationStrategy _evaluationStrategy = new DefaultEvaluationStrategy();

    // ============================================================
    //  Runtime State
    // ============================================================
    
    private readonly Stopwatch _stopwatch = new();
    private Action<UoTProgress>? _progressCallback;
    private TUoTResult? _cachedResult;

    public TUoTCoordinatorGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"T-UoT Coordinator [{CustomState.ExecutionId}] - Phase: {CustomState.Phase}");
    }

    #region Public API

    public void SetStrategies(
        IRuleMutationStrategy? ruleMutationStrategy = null,
        IEvaluationStrategy? evaluationStrategy = null,
        Action<UoTProgress>? progressCallback = null)
    {
        _ruleMutationStrategy = ruleMutationStrategy ?? _ruleMutationStrategy;
        _evaluationStrategy = evaluationStrategy ?? _evaluationStrategy;
        _progressCallback = progressCallback;
    }

    public TUoTResult GetResult() => _cachedResult ?? CreateEmptyResult();
    public TUoTExecutionPhase GetPhase() => CustomState.Phase;

    #endregion

    #region Event Handlers

    [EventHandler]
    public async Task HandleStartTUoTRequest(StartTUoTRequest request)
    {
        Logger.LogInformation("T-UoT Coordinator {Id} starting transformative reasoning for: {Problem}",
            Id, request.Problem[..Math.Min(50, request.Problem.Length)]);

        try
        {
            await InitializeExecutionAsync(request);
            
            // T-UoT three-step process
            await ExecuteStep1_ExposeRulesAsync(request);
            await ExecuteStep2_MutateRulesAsync();
            await ExecuteStep3_ExploreRuleSpacesAsync();
            await ExecuteStep4_EvaluateSolutionsAsync();

            await CompleteExecutionAsync(request);
        }
        catch (Exception ex)
        {
            await HandleExecutionFailureAsync(request, ex);
        }
    }

    #endregion

    #region Step 1: Expose Rules (Including Hidden Assumptions)

    /// <summary>
    /// Step 1: Expose all rules including hidden assumptions.
    /// Hidden assumptions are the most limiting - they're invisible cages.
    /// </summary>
    private async Task ExecuteStep1_ExposeRulesAsync(StartTUoTRequest request)
    {
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseExposingRules;
        ReportProgress(UoTPhase.RetrievingAnalogies, "T-UoT: Exposing rules and hidden assumptions...", 0.1f);

        // Step 1a: Extract explicit rules
        var explicitPrompt = _ruleMutationStrategy.BuildExplicitRuleExtractionPrompt(
            request.Problem, request.DomainHint);

        var explicitResponse = await ChatAsync(ChatRequest.Create(explicitPrompt));
        CustomState.TotalLlmCalls++;

        var explicitRules = _ruleMutationStrategy.ParseExplicitRules(explicitResponse.Content);
        
        foreach (var rule in explicitRules)
        {
            CustomState.ExposedRules.Add(new Rule
            {
                Id = rule.Id,
                Content = rule.Content,
                Type = rule.Type,
                IsExplicit = true,
                ConstraintStrength = rule.ConstraintStrength,
                Domain = rule.Domain
            });
        }

        Logger.LogDebug("Exposed {Count} explicit rules", explicitRules.Count);

        // Step 1b: Extract hidden assumptions (CRITICAL for T-UoT)
        var hiddenPrompt = _ruleMutationStrategy.BuildHiddenAssumptionExtractionPrompt(
            request.Problem, explicitRules, request.DomainHint);

        var hiddenResponse = await ChatAsync(ChatRequest.Create(hiddenPrompt));
        CustomState.TotalLlmCalls++;

        var hiddenAssumptions = _ruleMutationStrategy.ParseHiddenAssumptions(hiddenResponse.Content);

        foreach (var assumption in hiddenAssumptions)
        {
            CustomState.HiddenAssumptions.Add(new Rule
            {
                Id = assumption.Id,
                Content = assumption.Content,
                Type = RuleType.Assumption,
                IsExplicit = false,
                ConstraintStrength = assumption.ConstraintStrength,
                Domain = assumption.Domain
            });
        }

        Logger.LogDebug("Exposed {Count} hidden assumptions", hiddenAssumptions.Count);

        ReportProgress(UoTPhase.RetrievingAnalogies,
            $"Exposed {explicitRules.Count} rules + {hiddenAssumptions.Count} hidden assumptions",
            0.3f, analogiesFound: explicitRules.Count + hiddenAssumptions.Count);
    }

    #endregion

    #region Step 2: Rule Mutation

    /// <summary>
    /// Step 2: Generate mutated rule sets.
    /// Each mutation creates a new solution space to explore.
    /// Prioritize mutating hidden assumptions for maximum creative leverage.
    /// </summary>
    private async Task ExecuteStep2_MutateRulesAsync()
    {
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseMutatingRules;
        ReportProgress(UoTPhase.DecomposingThoughts, "T-UoT: Generating mutated rule sets...", 0.35f);

        // Combine explicit rules and hidden assumptions
        var allRules = CustomState.ExposedRules
            .Concat(CustomState.HiddenAssumptions)
            .Select(r => new ParsedRule(
                r.Id, r.Content, r.Type, r.IsExplicit, r.ConstraintStrength, r.Domain))
            .ToList();

        var mutationPrompt = _ruleMutationStrategy.BuildRuleMutationPrompt(
            allRules,
            CustomState.OriginalProblem,
            CustomConfig.MaxRuleSets,
            CustomConfig.MutationsPerSet);

        var mutationResponse = await ChatAsync(ChatRequest.Create(mutationPrompt));
        CustomState.TotalLlmCalls++;

        var mutatedSets = _ruleMutationStrategy.ParseMutatedRuleSets(mutationResponse.Content);

        foreach (var set in mutatedSets)
        {
            var mutatedRuleSet = new MutatedRuleSet
            {
                Id = set.Id,
                MutationRationale = set.MutationRationale,
                RadicalityScore = set.RadicalityScore
            };

            // Add original rules
            foreach (var origRule in allRules.Where(r => set.Mutations.Any(m => m.RuleId == r.Id)))
            {
                mutatedRuleSet.OriginalRules.Add(new Rule
                {
                    Id = origRule.Id,
                    Content = origRule.Content,
                    Type = origRule.Type,
                    IsExplicit = origRule.IsExplicit,
                    ConstraintStrength = origRule.ConstraintStrength,
                    Domain = origRule.Domain
                });
            }

            // Add mutations
            foreach (var mutation in set.Mutations)
            {
                mutatedRuleSet.Mutations.Add(new RuleMutation
                {
                    RuleId = mutation.RuleId,
                    MutationType = mutation.MutationType,
                    OriginalContent = mutation.OriginalContent,
                    MutatedContent = mutation.MutatedContent,
                    MutationRationale = mutation.Rationale
                });
            }

            CustomState.MutatedRuleSets.Add(mutatedRuleSet);
        }

        Logger.LogDebug("Generated {Count} mutated rule sets", mutatedSets.Count);

        ReportProgress(UoTPhase.DecomposingThoughts,
            $"Generated {mutatedSets.Count} alternative rule spaces",
            0.5f, thoughtsExtracted: mutatedSets.Count);
    }

    #endregion

    #region Step 3: Explore New Rule Spaces

    /// <summary>
    /// Step 3: Explore each mutated rule space for solutions.
    /// Solutions in these spaces were IMPOSSIBLE before rule mutation.
    /// This is where true transformation happens.
    /// </summary>
    private async Task ExecuteStep3_ExploreRuleSpacesAsync()
    {
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseExploringSpaces;
        ReportProgress(UoTPhase.Synthesizing, "T-UoT: Exploring new rule spaces...", 0.55f);

        foreach (var ruleSet in CustomState.MutatedRuleSets)
        {
            var parsedRuleSet = new ParsedMutatedRuleSet(
                ruleSet.Id,
                ruleSet.OriginalRules.Select(r => new ParsedRule(
                    r.Id, r.Content, r.Type, r.IsExplicit, r.ConstraintStrength, r.Domain)).ToList(),
                ruleSet.Mutations.Select(m => new ParsedRuleMutation(
                    m.RuleId, m.MutationType, m.OriginalContent, m.MutatedContent, m.MutationRationale)).ToList(),
                [], // Resulting rules derived
                ruleSet.MutationRationale,
                ruleSet.RadicalityScore);

            var explorationPrompt = _ruleMutationStrategy.BuildRuleSpaceExplorationPrompt(
                CustomState.OriginalProblem, parsedRuleSet);

            var explorationResponse = await ChatAsync(ChatRequest.Create(explorationPrompt));
            CustomState.TotalLlmCalls++;

            var solutions = _ruleMutationStrategy.ParseTransformativeSolutions(
                explorationResponse.Content, ruleSet.Id);

            foreach (var solution in solutions)
            {
                var transformative = new TransformativeSolution
                {
                    Id = solution.Id,
                    Content = solution.Content,
                    RuleSet = ruleSet,
                    CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                transformative.ViolatedConventions.AddRange(
                    solution.ViolatedConventions.Select(vc => new Rule { Content = vc }));
                
                CustomState.TransformativeSolutions.Add(transformative);
            }
        }

        Logger.LogDebug("Discovered {Count} transformative solutions", 
            CustomState.TransformativeSolutions.Count);

        ReportProgress(UoTPhase.Synthesizing,
            $"Discovered {CustomState.TransformativeSolutions.Count} transformative solutions",
            0.75f, candidatesGenerated: CustomState.TransformativeSolutions.Count);
    }

    #endregion

    #region Step 4: Evaluate Transformative Solutions

    /// <summary>
    /// Step 4: Evaluate solutions with four dimensions:
    /// Feasibility (given rule changes), Utility, Novelty, Radicality.
    /// </summary>
    private async Task ExecuteStep4_EvaluateSolutionsAsync()
    {
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseEvaluating;
        ReportProgress(UoTPhase.Evaluating, "T-UoT: Evaluating transformative solutions...", 0.8f);

        var existingSolutions = CustomState.TransformativeSolutions
            .Select(s => s.Content)
            .Take(5)
            .ToList();

        var passedSolutions = new List<TransformativeSolution>();

        foreach (var solution in CustomState.TransformativeSolutions)
        {
            var parsedSolution = new ParsedTransformativeSolution(
                solution.Id,
                solution.Content,
                solution.RuleSet?.Id ?? "",
                solution.ViolatedConventions.Select(r => r.Content).ToList());

            var evalPrompt = _ruleMutationStrategy.BuildTransformativeEvaluationPrompt(
                parsedSolution, CustomState.OriginalProblem, existingSolutions);

            var evalResponse = await ChatAsync(ChatRequest.Create(evalPrompt));
            CustomState.TotalLlmCalls++;

            var evaluation = _ruleMutationStrategy.ParseTransformativeEvaluation(evalResponse.Content);

            // Feasibility is still a gate (but potentially with different thresholds for transformative)
            if (evaluation.Feasibility < CustomConfig.FeasibilityThreshold)
            {
                Logger.LogDebug("Solution {Id} failed feasibility: {Score}", 
                    solution.Id, evaluation.Feasibility);
                continue;
            }

            // Radicality threshold for T-UoT
            if (evaluation.Radicality < CustomConfig.MinRadicality)
            {
                Logger.LogDebug("Solution {Id} not radical enough: {Score}", 
                    solution.Id, evaluation.Radicality);
                continue;
            }

            solution.Score = new CreativeScore
            {
                Feasibility = evaluation.Feasibility,
                Utility = evaluation.Utility,
                Novelty = evaluation.Novelty,
                // T-UoT composite includes radicality bonus
                Composite = ComputeTransformativeScore(evaluation),
                EvaluationRationale = evaluation.Rationale
            };

            passedSolutions.Add(solution);
        }

        // Rank by composite score
        var ranked = passedSolutions
            .OrderByDescending(s => s.Score.Composite)
            .ToList();

        CustomState.TransformativeSolutions.Clear();
        CustomState.TransformativeSolutions.AddRange(ranked);

        if (ranked.Count > 0)
            CustomState.BestSolution = ranked[0];

        ReportProgress(UoTPhase.Evaluating,
            $"{passedSolutions.Count} solutions passed (feasibility + radicality)",
            0.95f, candidatesPassed: passedSolutions.Count);
    }

    private float ComputeTransformativeScore(ParsedTransformativeEvaluation eval)
    {
        // T-UoT scoring: balance utility with novelty and radicality
        // Radicality is rewarded in T-UoT (unlike C-UoT where it's neutral)
        var utilityContrib = eval.Utility * CustomConfig.UtilityWeight;
        var noveltyContrib = eval.Novelty * CustomConfig.NoveltyWeight;
        var radicalityBonus = eval.Radicality * 0.2f;  // 20% bonus for radicality
        
        return (utilityContrib + noveltyContrib + radicalityBonus) / 
               (CustomConfig.UtilityWeight + CustomConfig.NoveltyWeight + 0.2f);
    }

    #endregion

    #region Helpers

    private async Task InitializeExecutionAsync(StartTUoTRequest request)
    {
        CustomState.ExecutionId = request.ExecutionId;
        CustomState.OriginalProblem = request.Problem;
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseIdle;
        CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        CustomConfig.MaxRuleSets = request.MaxRuleSets > 0 ? request.MaxRuleSets : 3;
        CustomConfig.MutationsPerSet = request.MutationsPerSet > 0 ? request.MutationsPerSet : 3;
        CustomConfig.MinRadicality = request.MinRadicality > 0 ? request.MinRadicality : 0.5f;
        CustomConfig.FeasibilityThreshold = request.FeasibilityThreshold > 0 ? request.FeasibilityThreshold : 0.5f;  // Lower for T-UoT
        CustomConfig.UtilityWeight = 0.4f;
        CustomConfig.NoveltyWeight = 0.4f;  // Higher novelty weight for T-UoT

        CustomState.FeasibilityThreshold = CustomConfig.FeasibilityThreshold;
        CustomState.MaxRuleSets = CustomConfig.MaxRuleSets;

        await InitializeAsync(request.ProviderName, config =>
        {
            config.Temperature = 0.9f;  // Highest temperature for transformative thinking
            config.MaxOutputTokens = 4096;
        });

        _stopwatch.Restart();
        ReportProgress(UoTPhase.Starting, "T-UoT: Transformative Creative Reasoning initialized", 0.05f);
    }

    private async Task CompleteExecutionAsync(StartTUoTRequest request)
    {
        _stopwatch.Stop();
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseCompleted;
        CustomState.CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        _cachedResult = new TUoTResult
        {
            Success = CustomState.BestSolution != null,
            BestSolution = CustomState.BestSolution,
            AllSolutions = CustomState.TransformativeSolutions.ToList(),
            Trace = new TUoTResultTrace
            {
                ExecutionId = CustomState.ExecutionId,
                OriginalProblem = CustomState.OriginalProblem,
                RulesExposed = CustomState.ExposedRules.Count,
                HiddenAssumptionsFound = CustomState.HiddenAssumptions.Count,
                RuleSetsExplored = CustomState.MutatedRuleSets.Count,
                SolutionsGenerated = CustomState.TransformativeSolutions.Count,
                TotalLLMCalls = CustomState.TotalLlmCalls,
                TotalTokens = CustomState.TotalTokens,
                Duration = _stopwatch.Elapsed,
                ExposedRules = CustomState.ExposedRules.ToList(),
                HiddenAssumptions = CustomState.HiddenAssumptions.ToList(),
                MutatedRuleSets = CustomState.MutatedRuleSets.ToList()
            }
        };

        ReportProgress(UoTPhase.Completed,
            CustomState.BestSolution != null
                ? $"T-UoT complete! Score: {CustomState.BestSolution.Score?.Composite:F2} | Rules challenged: {CustomState.HiddenAssumptions.Count}"
                : "T-UoT complete (no viable transformative solutions)",
            1.0f);

        await PublishAsync(new TUoTCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = _cachedResult.Success,
            BestSolution = CustomState.BestSolution,
            RuleSetsExplored = CustomState.MutatedRuleSets.Count,
            RulesExposed = CustomState.ExposedRules.Count,
            AssumptionsChallenged = CustomState.HiddenAssumptions.Count,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            TotalTokens = CustomState.TotalTokens,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

    private async Task HandleExecutionFailureAsync(StartTUoTRequest request, Exception ex)
    {
        Logger.LogError(ex, "T-UoT Coordinator {Id} failed", Id);
        _stopwatch.Stop();
        CustomState.Phase = TUoTExecutionPhase.TuotPhaseFailed;
        _cachedResult = CreateEmptyResult(ex.Message);

        await PublishAsync(new TUoTCompleted
        {
            ExecutionId = request.ExecutionId,
            Success = false,
            Error = ex.Message,
            TotalLlmCalls = CustomState.TotalLlmCalls,
            DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
        });
    }

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

    private TUoTResult CreateEmptyResult(string? error = null) => new()
    {
        Success = false, Error = error ?? "Not available",
        Trace = new TUoTResultTrace
        {
            ExecutionId = CustomState.ExecutionId ?? "",
            OriginalProblem = CustomState.OriginalProblem ?? "",
            Duration = _stopwatch.Elapsed
        }
    };

    #endregion
}

