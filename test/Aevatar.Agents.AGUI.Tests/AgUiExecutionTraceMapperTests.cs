using System.Text.Json;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AGUI;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.AGUI.Tests;

public class AgUiExecutionTraceMapperTests
{
    [Fact]
    public void Map_ShouldIncludeWinnerFields_InCustomEventPayload()
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            Phase = "vote",
            Message = "consensus reached",
            NodeId = "dag_consensus:decompose"
        };

        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventStatus.Completed);
        evt.Fields[ExecutionTraceEventMakerFields.WinnerProposalId] =
            ExecutionTraceEventFieldValue.FromString("decompose.gen[1]");
        evt.Fields[ExecutionTraceEventMakerFields.WinnerHash] =
            ExecutionTraceEventFieldValue.FromString("ABCDEF1234567890");
        evt.Fields[ExecutionTraceEventMakerFields.WinnerVotes] =
            ExecutionTraceEventFieldValue.FromInt(3);
        evt.Fields[ExecutionTraceEventMakerFields.WinnerRunnerUpVotes] =
            ExecutionTraceEventFieldValue.FromInt(1);
        evt.Fields[ExecutionTraceEventMakerFields.WinnerClusterCount] =
            ExecutionTraceEventFieldValue.FromInt(2);
        evt.Fields[ExecutionTraceEventMakerFields.WinnerSemantic] =
            ExecutionTraceEventFieldValue.FromBool(true);
        evt.Fields[ExecutionTraceEventMakerFields.WinnerIsConsensus] =
            ExecutionTraceEventFieldValue.FromBool(true);

        var mapped = AgUiExecutionTraceMapper.Map(evt);
        var custom = mapped.OfType<CustomEvent>()
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
}
