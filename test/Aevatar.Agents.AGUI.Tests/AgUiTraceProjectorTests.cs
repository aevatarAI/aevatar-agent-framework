using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AGUI;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.AGUI.Tests;

public class AgUiTraceProjectorTests
{
    [Fact]
    public void Map_SessionStart_ShouldEmitRunStarted()
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            Phase = ExecutionTraceEventPhase.SessionStart,
            Message = "session start",
            NodeId = "session:s1"
        };
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString("s1");
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString("run1");
        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventStatus.Running);

        var mapped = AgUiTraceProjector.Map(evt);
        mapped.Count.ShouldBe(1);
        var run = mapped[0].ShouldBeOfType<RunStartedEvent>();
        run.ThreadId.ShouldBe("s1");
        run.RunId.ShouldBe("run1");
    }

    [Fact]
    public void Map_ToolProgress_ShouldEmitToolCallResult()
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            Phase = ExecutionTraceEventPhase.ToolProgress,
            Message = "working",
            NodeId = "tool:tc1"
        };
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString("s1");
        evt.Fields[ExecutionTraceEventFields.ToolCallId] =
            ExecutionTraceEventFieldValue.FromString("tc1");
        evt.Fields[ExecutionTraceEventFields.ToolName] =
            ExecutionTraceEventFieldValue.FromString("bash");

        var mapped = AgUiTraceProjector.Map(evt);
        mapped.Count.ShouldBe(1);
        var tool = mapped[0].ShouldBeOfType<ToolCallResultEvent>();
        tool.ToolCallId.ShouldBe("tc1");
        tool.Result.ShouldBe("working");
    }

    [Fact]
    public void Map_UnknownPhase_ShouldFallbackToExecutionTraceMapper()
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            Phase = "workflow.step",
            Message = "running",
            NodeId = "step-1"
        };
        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventStatus.Running);

        var mapped = AgUiTraceProjector.Map(evt);
        mapped.ShouldContain(e => e is StepStartedEvent);
        mapped.ShouldContain(e => e is CustomEvent);
    }
}
