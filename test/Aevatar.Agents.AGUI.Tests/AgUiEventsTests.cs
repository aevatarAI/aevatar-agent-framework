using System.Text.Json;
using Aevatar.Agents.AGUI;
using Shouldly;

namespace Aevatar.Agents.AGUI.Tests;

public class AgUiEventsTests
{
    [Fact]
    public void EventTypeConstants_ShouldBeStable()
    {
        new RunStartedEvent { ThreadId = "t", RunId = "r" }.Type.ShouldBe("RUN_STARTED");
        new RunFinishedEvent { ThreadId = "t", RunId = "r" }.Type.ShouldBe("RUN_FINISHED");
        new RunErrorEvent { Message = "m" }.Type.ShouldBe("RUN_ERROR");

        new StepStartedEvent { StepName = "s" }.Type.ShouldBe("STEP_STARTED");
        new StepFinishedEvent { StepName = "s" }.Type.ShouldBe("STEP_FINISHED");

        new TextMessageStartEvent { MessageId = "m", Role = "assistant" }.Type.ShouldBe("TEXT_MESSAGE_START");
        new TextMessageContentEvent { MessageId = "m", Delta = "x" }.Type.ShouldBe("TEXT_MESSAGE_CONTENT");
        new TextMessageEndEvent { MessageId = "m" }.Type.ShouldBe("TEXT_MESSAGE_END");

        new StateSnapshotEvent { Snapshot = new { ok = true } }.Type.ShouldBe("STATE_SNAPSHOT");
        new StateDeltaEvent { Delta = new object[] { new { op = "add" } } }.Type.ShouldBe("STATE_DELTA");
        new MessagesSnapshotEvent { Messages = new List<AgUiMessage>() }.Type.ShouldBe("MESSAGES_SNAPSHOT");
        new CustomEvent { Name = "n" }.Type.ShouldBe("CUSTOM");
        
        // Tool events
        new ToolCallStartEvent { MessageId = "m", ToolCallId = "c", ToolName = "t" }.Type.ShouldBe("TOOL_CALL_START");
        new ToolCallArgsEvent { MessageId = "m", ToolCallId = "c", ArgsDelta = "a" }.Type.ShouldBe("TOOL_CALL_ARGS");
        new ToolCallEndEvent { MessageId = "m", ToolCallId = "c" }.Type.ShouldBe("TOOL_CALL_END");
        new ToolCallResultEvent { MessageId = "m", ToolCallId = "c", Result = "r" }.Type.ShouldBe("TOOL_CALL_RESULT");
    }

    [Fact]
    public void ToolCallEvents_ShouldSerialize_Correctly()
    {
        var evt = new ToolCallStartEvent
        {
            Timestamp = 123456789,
            MessageId = "msg-123",
            ToolCallId = "call-abc",
            ToolName = "get_weather"
        };
        
        var json = JsonSerializer.Serialize(evt);
        json.ShouldContain("TOOL_CALL_START");
        json.ShouldContain("get_weather");
        json.ShouldContain("msg-123");
    }


    [Fact]
    public void MessagesSnapshotEvent_ShouldSerialize_ToJson()
    {
        // NOTE:
        // AGUI library itself does not enforce camelCase; host typically configures it.
        // This test only asserts that the model is JSON-serializable without throwing.
        var evt = new MessagesSnapshotEvent
        {
            Timestamp = 123,
            Messages = new List<AgUiMessage>
            {
                new() { Id = "1", Role = "assistant", Content = "hello" },
                new() { Id = "2", Role = "tool", Content = "{}", ToolCallId = "call-1", Name = "my_tool" }
            }
        };

        var json = JsonSerializer.Serialize(evt);
        json.ShouldContain("\"Type\"");
        json.ShouldContain("MESSAGES_SNAPSHOT");
        json.ShouldContain("\"Messages\"");
    }
}


