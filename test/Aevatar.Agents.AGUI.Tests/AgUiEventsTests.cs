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


