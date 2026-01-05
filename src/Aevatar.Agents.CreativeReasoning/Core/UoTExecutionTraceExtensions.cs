using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.CreativeReasoning.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.CreativeReasoning.Core;

// ============================================================
//  UoT -> ExecutionTrace Adapter
//
//  WHY:
//  - UoT result trace is currently a local C# class (`UoTResultTrace`/`TUoTResultTrace`).
//  - Exportable / cross-runtime trace must converge to Protobuf `ExecutionTrace`.
//
//  MAPPING:
//  - Root node: "UoT" execution
//  - Candidate ranking: one generic DecisionSession (score-based)
// ============================================================
public static class UoTExecutionTraceExtensions
{
    public static ExecutionTrace ToExecutionTrace(this UoTResult result)
    {
        var now = DateTime.UtcNow;
        var duration = result.Trace.Duration;
        var startedAt = now - duration;

        var trace = new ExecutionTrace
        {
            ExecutionId = result.Trace.ExecutionId,
            Kind = ExecutionTraceKind.Uot,
            Status = result.Success
                ? ExecutionTraceStatus.Succeeded
                : ExecutionTraceStatus.Failed,
            Name = "UoT",
            Description = Preview(result.Trace.OriginalProblem, 200),
            StartedAt = Timestamp.FromDateTime(startedAt),
            EndedAt = Timestamp.FromDateTime(now),
            Cost = new ExecutionTraceCost
            {
                DurationMs = (long)duration.TotalMilliseconds,
                TotalLlmCalls = result.Trace.TotalLLMCalls,
                TotalTokens = result.Trace.TotalTokens
            },
            Error = result.Error ?? string.Empty
        };

        trace.Metrics["analogies_explored"] = Cv(result.Trace.AnalogiesExplored);
        trace.Metrics["thoughts_extracted"] = Cv(result.Trace.ThoughtsExtracted);
        trace.Metrics["candidates_generated"] = Cv(result.Trace.CandidatesGenerated);
        trace.Metrics["candidates_passed_feasibility"] = Cv(result.Trace.CandidatesPassedFeasibility);

        trace.Root = BuildUoTRootNode(result);
        return trace;
    }

    public static ExecutionTrace ToExecutionTrace(this TUoTResult result)
    {
        var now = DateTime.UtcNow;
        var duration = result.Trace.Duration;
        var startedAt = now - duration;

        var trace = new ExecutionTrace
        {
            ExecutionId = result.Trace.ExecutionId,
            Kind = ExecutionTraceKind.Uot,
            Status = result.Success
                ? ExecutionTraceStatus.Succeeded
                : ExecutionTraceStatus.Failed,
            Name = "T-UoT",
            Description = Preview(result.Trace.OriginalProblem, 200),
            StartedAt = Timestamp.FromDateTime(startedAt),
            EndedAt = Timestamp.FromDateTime(now),
            Cost = new ExecutionTraceCost
            {
                DurationMs = (long)duration.TotalMilliseconds,
                TotalLlmCalls = result.Trace.TotalLLMCalls,
                TotalTokens = result.Trace.TotalTokens
            },
            Error = result.Error ?? string.Empty
        };

        trace.Metrics["rules_exposed"] = Cv(result.Trace.RulesExposed);
        trace.Metrics["hidden_assumptions_found"] = Cv(result.Trace.HiddenAssumptionsFound);
        trace.Metrics["rule_sets_explored"] = Cv(result.Trace.RuleSetsExplored);
        trace.Metrics["solutions_generated"] = Cv(result.Trace.SolutionsGenerated);

        trace.Root = BuildTUoTRootNode(result);
        return trace;
    }

    private static ExecutionTraceNode BuildUoTRootNode(UoTResult result)
    {
        var node = new ExecutionTraceNode
        {
            NodeId = result.Trace.ExecutionId,
            Name = "UoT",
            Type = "uot",
            Description = result.Trace.OriginalProblem,
            Status = result.Success
                ? ExecutionTraceStatus.Succeeded
                : ExecutionTraceStatus.Failed,
            Output = result.BestSolution?.Content ?? string.Empty,
            Error = result.Error ?? string.Empty
        };

        // Score-based ranking as a generic decision session.
        node.Decisions.Add(BuildCandidateRankingDecision(
            decisionId: $"{result.Trace.ExecutionId}:candidate_ranking",
            winnerId: result.BestSolution?.Id ?? string.Empty,
            candidates: result.AllCandidates));

        return node;
    }

    private static ExecutionTraceNode BuildTUoTRootNode(TUoTResult result)
    {
        var node = new ExecutionTraceNode
        {
            NodeId = result.Trace.ExecutionId,
            Name = "T-UoT",
            Type = "tuot",
            Description = result.Trace.OriginalProblem,
            Status = result.Success
                ? ExecutionTraceStatus.Succeeded
                : ExecutionTraceStatus.Failed,
            Output = result.BestSolution?.Content ?? string.Empty,
            Error = result.Error ?? string.Empty
        };

        node.Decisions.Add(BuildTransformativeRankingDecision(
            decisionId: $"{result.Trace.ExecutionId}:transformative_ranking",
            winnerId: result.BestSolution?.Id ?? string.Empty,
            solutions: result.AllSolutions));

        return node;
    }

    private static ExecutionTraceDecisionSession BuildCandidateRankingDecision(
        string decisionId,
        string winnerId,
        IReadOnlyList<CandidateSolution> candidates)
    {
        var decision = new ExecutionTraceDecisionSession
        {
            DecisionId = decisionId,
            Type = "ranking",
            Rounds = 1,
            WinnerCandidateId = winnerId
        };

        foreach (var c in candidates)
        {
            decision.Candidates.Add(new ExecutionTraceCandidate
            {
                CandidateId = c.Id,
                Content = c.Content,
                Votes = 0,
                Score = c.Score?.Composite ?? 0,
                Metrics =
                {
                    { "feasibility", Cv(c.Score?.Feasibility ?? 0) },
                    { "utility", Cv(c.Score?.Utility ?? 0) },
                    { "novelty", Cv(c.Score?.Novelty ?? 0) },
                    { "composite", Cv(c.Score?.Composite ?? 0) }
                }
            });
        }

        return decision;
    }

    private static ExecutionTraceDecisionSession BuildTransformativeRankingDecision(
        string decisionId,
        string winnerId,
        IReadOnlyList<TransformativeSolution> solutions)
    {
        var decision = new ExecutionTraceDecisionSession
        {
            DecisionId = decisionId,
            Type = "ranking",
            Rounds = 1,
            WinnerCandidateId = winnerId
        };

        foreach (var s in solutions)
        {
            decision.Candidates.Add(new ExecutionTraceCandidate
            {
                CandidateId = s.Id,
                Content = s.Content,
                Votes = 0,
                Score = s.Score?.Composite ?? 0,
                Metrics =
                {
                    { "feasibility", Cv(s.Score?.Feasibility ?? 0) },
                    { "utility", Cv(s.Score?.Utility ?? 0) },
                    { "novelty", Cv(s.Score?.Novelty ?? 0) },
                    { "composite", Cv(s.Score?.Composite ?? 0) }
                }
            });
        }

        return decision;
    }

    private static Aevatar.Agents.ContextValue Cv(double value)
    {
        return new Aevatar.Agents.ContextValue { DoubleValue = value };
    }

    private static Aevatar.Agents.ContextValue Cv(int value)
    {
        return new Aevatar.Agents.ContextValue { IntValue = value };
    }

    private static string Preview(string content, int maxChars)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
        return content.Length <= maxChars ? content : content[..maxChars] + "...";
    }
}


