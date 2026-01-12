using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Core.AgenticRag;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Tests.TestAgents;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class AgenticRagLoopTests
{
    [Fact]
    public async Task AnswerAsync_ShouldStopWithBudgetExceeded_WhenCriticNeverPasses()
    {
        var planner = new FixedPlanner();
        var retriever = new FixedRetriever(_ => new[] { BuildEvidence("e1", "snippet-1") });
        var synthesizer = new FixedSynthesizer(_ => "draft");
        var critic = new FixedCritic(_ => false);

        var agent = new AgenticRagTestAgent(planner, retriever, synthesizer, critic);

        var resp = await agent.AnswerAsync(new AgenticRagRequest
        {
            RequestId = "r1",
            Query = "q",
            Budget = new RagBudget
            {
                MaxIterations = 2,
                MaxEvidenceItems = 1
            }
        });

        resp.StopReason.ShouldBe(RagStopReason.BudgetExceeded);
        resp.Iterations.ShouldBe(2);
        critic.CallCount.ShouldBe(2);
        planner.CallCount.ShouldBe(2);
        retriever.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task AnswerAsync_ShouldStopWithNoEvidence_WhenRetrieverAlwaysReturnsEmpty()
    {
        var planner = new FixedPlanner();
        var retriever = new FixedRetriever(_ => Array.Empty<RagEvidenceSummary>());
        var synthesizer = new FixedSynthesizer(_ => "draft");
        var critic = new FixedCritic(_ => false);

        var agent = new AgenticRagTestAgent(planner, retriever, synthesizer, critic);

        var resp = await agent.AnswerAsync(new AgenticRagRequest
        {
            RequestId = "r2",
            Query = "q",
            Budget = new RagBudget
            {
                MaxIterations = 2,
                MaxEvidenceItems = 3
            }
        });

        resp.StopReason.ShouldBe(RagStopReason.NoEvidence);
        resp.Iterations.ShouldBe(2);
        resp.Evidence.Count.ShouldBe(0);
    }

    [Fact]
    public async Task AnswerAsync_ShouldBounceBackUntilCriticPasses()
    {
        var planner = new FixedPlanner();
        var retriever = new FixedRetriever(_ => new[] { BuildEvidence("e1", "snippet-1") });
        var synthesizer = new FixedSynthesizer(_ => "draft");

        // Fail first critique, pass second.
        var critic = new FixedCritic(call => call >= 2);

        var agent = new AgenticRagTestAgent(planner, retriever, synthesizer, critic);

        var resp = await agent.AnswerAsync(new AgenticRagRequest
        {
            RequestId = "r3",
            Query = "q",
            Budget = new RagBudget
            {
                MaxIterations = 3,
                MaxEvidenceItems = 1
            }
        });

        resp.StopReason.ShouldBe(RagStopReason.Succeeded);
        resp.Iterations.ShouldBe(2);
        critic.CallCount.ShouldBe(2);
    }

    private static RagEvidenceSummary BuildEvidence(string entryId, string snippet)
    {
        return new RagEvidenceSummary
        {
            EvidenceId = entryId,
            Snippet = snippet,
            Citation = new RagCitation
            {
                MemoryEntry = new MemoryEntryCitation
                {
                    MemoryId = "privateagent::agent-1",
                    EntryId = entryId,
                    Scope = new MemoryScope
                    {
                        Type = MemoryScopeType.PrivateAgent,
                        ScopeId = "agent-1"
                    }
                }
            },
            Score = 0.5
        };
    }

    private sealed class FixedPlanner : IAgenticRagPlanner
    {
        public int CallCount { get; private set; }
        public string Name => "test_planner";

        public Task<RagPlan> PlanAsync(
            AgenticRagRequest request,
            int iteration,
            IReadOnlyList<RagEvidenceSummary> evidenceSoFar,
            CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(new RagPlan
            {
                Retrievals = new[]
                {
                    new RagRetrieveRequest
                    {
                        RequestId = request.RequestId,
                        Query = request.Query,
                        MaxResults = 10,
                        MaxSnippetChars = 400,
                        Scope = request.Scope
                    }
                }
            });
        }
    }

    private sealed class FixedRetriever : IAgenticRagRetriever
    {
        private readonly Func<int, IReadOnlyList<RagEvidenceSummary>> _factory;
        public int CallCount { get; private set; }
        public string Name => "test_retriever";

        public FixedRetriever(Func<int, IReadOnlyList<RagEvidenceSummary>> factory)
        {
            _factory = factory;
        }

        public Task<IReadOnlyList<RagEvidenceSummary>> RetrieveAsync(
            RagRetrieveRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_factory(CallCount));
        }
    }

    private sealed class FixedSynthesizer : IAgenticRagSynthesizer
    {
        private readonly Func<int, string> _factory;
        public int CallCount { get; private set; }
        public string Name => "test_synth";

        public FixedSynthesizer(Func<int, string> factory)
        {
            _factory = factory;
        }

        public Task<RagSynthesisResult> SynthesizeAsync(
            AgenticRagRequest request,
            IReadOnlyList<RagEvidenceSummary> evidence,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RagSynthesisResult
            {
                Draft = _factory(CallCount),
                UsedEvidence = evidence.ToList(),
                Diagnostics = new Dictionary<string, string>
                {
                    ["draft_at"] = Timestamp.FromDateTime(DateTime.UtcNow).ToString()
                }
            });
        }
    }

    private sealed class FixedCritic : IAgenticRagCritic
    {
        private readonly Func<int, bool> _passWhen;
        public int CallCount { get; private set; }
        public string Name => "test_critic";

        public FixedCritic(Func<int, bool> passWhen)
        {
            _passWhen = passWhen;
        }

        public Task<RagCritiqueResult> CritiqueAsync(
            AgenticRagRequest request,
            RagSynthesisResult draft,
            IReadOnlyList<RagEvidenceSummary> evidence,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var passed = _passWhen(CallCount);
            return Task.FromResult(new RagCritiqueResult
            {
                Passed = passed,
                Gaps = passed ? Array.Empty<RagGap>() : new[] { new RagGap { Description = "missing evidence" } }
            });
        }
    }
}


