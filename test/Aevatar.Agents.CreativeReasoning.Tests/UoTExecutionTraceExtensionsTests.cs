using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Messages;
using Shouldly;

namespace Aevatar.Agents.CreativeReasoning.Tests;

public class UoTExecutionTraceExtensionsTests
{
    [Fact]
    public void ToExecutionTrace_UoT_ShouldMapCoreFields_AndCandidateRankingDecision()
    {
        var candidates = new List<CandidateSolution>
        {
            new()
            {
                Id = "c1",
                Content = "candidate-1",
                Score = new CreativeScore { Feasibility = 0.1f, Utility = 0.2f, Novelty = 0.3f, Composite = 0.4f }
            },
            new()
            {
                Id = "c2",
                Content = "candidate-2",
                Score = new CreativeScore { Feasibility = 0.6f, Utility = 0.7f, Novelty = 0.8f, Composite = 0.9f }
            }
        };

        var result = new UoTResult
        {
            Success = true,
            BestSolution = candidates[1],
            AllCandidates = candidates,
            Trace = new UoTResultTrace
            {
                ExecutionId = "exec-1",
                OriginalProblem = "very long problem text that should be used as root description and preview",
                Duration = TimeSpan.FromSeconds(3),
                TotalLLMCalls = 7,
                TotalTokens = 1234,
                AnalogiesExplored = 2,
                ThoughtsExtracted = 10,
                CandidatesGenerated = 5,
                CandidatesPassedFeasibility = 3
            }
        };

        var trace = result.ToExecutionTrace();

        trace.ExecutionId.ShouldBe("exec-1");
        trace.Kind.ShouldBe(ExecutionTraceKind.Uot);
        trace.Status.ShouldBe(ExecutionTraceStatus.Succeeded);
        trace.Name.ShouldBe("UoT");
        trace.Cost.DurationMs.ShouldBeGreaterThan(0);
        trace.Cost.TotalLlmCalls.ShouldBe(7);
        trace.Cost.TotalTokens.ShouldBe(1234);
        trace.Error.ShouldBe(string.Empty);

        trace.Metrics.ContainsKey("analogies_explored").ShouldBeTrue();
        trace.Metrics.ContainsKey("thoughts_extracted").ShouldBeTrue();
        trace.Metrics.ContainsKey("candidates_generated").ShouldBeTrue();
        trace.Metrics.ContainsKey("candidates_passed_feasibility").ShouldBeTrue();

        trace.Root.ShouldNotBeNull();
        trace.Root.NodeId.ShouldBe("exec-1");
        trace.Root.Type.ShouldBe("uot");
        trace.Root.Status.ShouldBe(ExecutionTraceStatus.Succeeded);
        trace.Root.Output.ShouldBe("candidate-2");

        trace.Root.Decisions.Count.ShouldBe(1);
        var decision = trace.Root.Decisions[0];
        decision.Type.ShouldBe("ranking");
        decision.WinnerCandidateId.ShouldBe("c2");
        decision.Candidates.Count.ShouldBe(2);
        // NOTE: CreativeScore.Composite is float; mapped Score may be a double with float->double rounding.
        var c2 = decision.Candidates.Single(c => c.CandidateId == "c2");
        c2.Score.ShouldBeGreaterThan(0.89);
        c2.Score.ShouldBeLessThan(0.91);
    }

    [Fact]
    public void ToExecutionTrace_UoT_ShouldPreviewDescription_WhenOriginalProblemTooLong()
    {
        var longProblem = new string('x', 300);
        var result = new UoTResult
        {
            Success = true,
            BestSolution = new CandidateSolution { Id = "c1", Content = "ok" },
            AllCandidates = new List<CandidateSolution> { new() { Id = "c1", Content = "ok" } },
            Trace = new UoTResultTrace
            {
                ExecutionId = "exec-long",
                OriginalProblem = longProblem,
                Duration = TimeSpan.FromMilliseconds(10),
                TotalLLMCalls = 0,
                TotalTokens = 0,
                AnalogiesExplored = 0,
                ThoughtsExtracted = 0,
                CandidatesGenerated = 0,
                CandidatesPassedFeasibility = 0
            }
        };

        var trace = result.ToExecutionTrace();
        trace.Description.Length.ShouldBeLessThanOrEqualTo(203); // 200 + "..."
        trace.Description.EndsWith("...").ShouldBeTrue();
    }

    [Fact]
    public void ToExecutionTrace_UoT_ShouldMapMetricsToContextValueInts()
    {
        var result = new UoTResult
        {
            Success = true,
            BestSolution = new CandidateSolution { Id = "c1", Content = "ok" },
            AllCandidates = new List<CandidateSolution> { new() { Id = "c1", Content = "ok" } },
            Trace = new UoTResultTrace
            {
                ExecutionId = "exec-metrics",
                OriginalProblem = "p",
                Duration = TimeSpan.FromMilliseconds(1),
                TotalLLMCalls = 1,
                TotalTokens = 2,
                AnalogiesExplored = 3,
                ThoughtsExtracted = 4,
                CandidatesGenerated = 5,
                CandidatesPassedFeasibility = 6
            }
        };

        var trace = result.ToExecutionTrace();
        trace.Metrics["analogies_explored"].IntValue.ShouldBe(3);
        trace.Metrics["thoughts_extracted"].IntValue.ShouldBe(4);
        trace.Metrics["candidates_generated"].IntValue.ShouldBe(5);
        trace.Metrics["candidates_passed_feasibility"].IntValue.ShouldBe(6);
    }

    [Fact]
    public void ToExecutionTrace_TUoT_ShouldMapCoreFields_AndTransformativeRankingDecision()
    {
        var solutions = new List<TransformativeSolution>
        {
            new()
            {
                Id = "s1",
                Content = "solution-1",
                Score = new CreativeScore { Feasibility = 0.2f, Utility = 0.3f, Novelty = 0.4f, Composite = 0.5f }
            }
        };

        var result = new TUoTResult
        {
            Success = false,
            BestSolution = solutions[0],
            AllSolutions = solutions,
            Error = "boom",
            Trace = new TUoTResultTrace
            {
                ExecutionId = "exec-2",
                OriginalProblem = "problem",
                Duration = TimeSpan.FromSeconds(1),
                TotalLLMCalls = 1,
                TotalTokens = 2,
                RulesExposed = 3,
                HiddenAssumptionsFound = 4,
                RuleSetsExplored = 5,
                SolutionsGenerated = 6
            }
        };

        var trace = result.ToExecutionTrace();
        trace.ExecutionId.ShouldBe("exec-2");
        trace.Name.ShouldBe("T-UoT");
        trace.Status.ShouldBe(ExecutionTraceStatus.Failed);
        trace.Error.ShouldBe("boom");

        trace.Root.ShouldNotBeNull();
        trace.Root.Type.ShouldBe("tuot");
        trace.Root.Status.ShouldBe(ExecutionTraceStatus.Failed);

        trace.Root.Decisions.Count.ShouldBe(1);
        trace.Root.Decisions[0].WinnerCandidateId.ShouldBe("s1");
        trace.Metrics.ContainsKey("rules_exposed").ShouldBeTrue();
        trace.Metrics.ContainsKey("hidden_assumptions_found").ShouldBeTrue();
        trace.Metrics.ContainsKey("rule_sets_explored").ShouldBeTrue();
        trace.Metrics.ContainsKey("solutions_generated").ShouldBeTrue();
    }
}


