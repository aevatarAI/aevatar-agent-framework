using System.Reflection;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Messages;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public sealed class StepEventsWinnerMappingTests
{
    [Fact]
    public void BuildExecutionTraceEvent_ShouldIncludeWinnerFields()
    {
        var stepEvent = new WorkflowStepEvent
        {
            RunId = "run_1",
            WorkflowName = "maker",
            StepId = "decompose",
            StepType = "vote",
            Status = StepStatus.Completed,
            Progress = 1.0f,
            Message = "consensus",
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            WinnerProposalId = "decompose.gen[1]",
            WinnerHash = "ABCDEF1234567890",
            WinnerVotes = 3,
            WinnerRunnerUpVotes = 1,
            WinnerClusterCount = 2,
            WinnerSemantic = true,
            WinnerIsConsensus = true
        };

        var method = typeof(CoordinatorAgent).GetMethod(
            "BuildExecutionTraceEvent",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull();

        var trace = (ExecutionTraceEvent)method!.Invoke(
            null,
            new object?[] { stepEvent, stepEvent.Message, "session_1", "coordinator_1" })!;

        trace.Fields[ExecutionTraceEventMakerFields.WinnerProposalId].StringValue.ShouldBe("decompose.gen[1]");
        trace.Fields[ExecutionTraceEventMakerFields.WinnerHash].StringValue.ShouldBe("ABCDEF1234567890");
        trace.Fields[ExecutionTraceEventMakerFields.WinnerVotes].IntValue.ShouldBe(3);
        trace.Fields[ExecutionTraceEventMakerFields.WinnerRunnerUpVotes].IntValue.ShouldBe(1);
        trace.Fields[ExecutionTraceEventMakerFields.WinnerClusterCount].IntValue.ShouldBe(2);
        trace.Fields[ExecutionTraceEventMakerFields.WinnerSemantic].BoolValue.ShouldBeTrue();
        trace.Fields[ExecutionTraceEventMakerFields.WinnerIsConsensus].BoolValue.ShouldBeTrue();
    }
}
