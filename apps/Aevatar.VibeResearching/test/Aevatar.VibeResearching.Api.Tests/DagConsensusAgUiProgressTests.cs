using System.Reflection;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Core;
using Shouldly;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe;

namespace VibeResearching.Api.Tests;

public sealed class DagConsensusAgUiProgressTests
{
    [Fact]
    public async Task Progress_ShouldEmitWinnerFields_InCustomEvent()
    {
        var session = new ResearchSession("s1");
        var progress = CreateProgressReporter(session, runId: "run_1", workflowName: "maker");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = session.Events.SubscribeAsync(replay: false, ct: cts.Token).GetAsyncEnumerator();

        try
        {
            var moveNext1 = enumerator.MoveNextAsync().AsTask();

            progress.Report(new ReasoningProgress
            {
                Phase = "vote",
                ProgressPercent = 1.0f,
                Message = "consensus reached",
                StepId = "decompose",
                StepType = "vote",
                StepStatus = "completed",
                WinnerProposalId = "decompose.gen[1]",
                WinnerHash = "ABCDEF1234567890",
                WinnerVotes = 3,
                WinnerRunnerUpVotes = 1,
                WinnerClusterCount = 2,
                WinnerSemantic = true,
                WinnerIsConsensus = true
            });

            (await moveNext1).ShouldBeTrue();
            var first = enumerator.Current;

            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            var second = enumerator.Current;

            var custom = new[] { first, second }
                .OfType<CustomEvent>()
                .Single(e => e.Name == AgUiExecutionTraceMapper.WorkflowExecutionEventName);

            var json = JsonSerializer.Serialize(custom);
            using var doc = JsonDocument.Parse(json);

            var fields = doc.RootElement.GetProperty("Value").GetProperty("fields");
            fields.GetProperty("winner_proposal_id").GetString().ShouldBe("decompose.gen[1]");
            fields.GetProperty("winner_hash").GetString().ShouldBe("ABCDEF1234567890");
            fields.GetProperty("winner_votes").GetInt32().ShouldBe(3);
            fields.GetProperty("winner_runner_up_votes").GetInt32().ShouldBe(1);
            fields.GetProperty("winner_cluster_count").GetInt32().ShouldBe(2);
            fields.GetProperty("winner_semantic").GetBoolean().ShouldBeTrue();
            fields.GetProperty("winner_is_consensus").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static IProgress<ReasoningProgress> CreateProgressReporter(
        ResearchSession session,
        string runId,
        string workflowName)
    {
        var method = typeof(VibeOrchestrator).GetMethod(
            "BuildDagConsensusAgUiProgress",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull();

        var progress = method!.Invoke(null, new object?[] { session, runId, workflowName });
        progress.ShouldNotBeNull();

        return (IProgress<ReasoningProgress>)progress!;
    }
}
