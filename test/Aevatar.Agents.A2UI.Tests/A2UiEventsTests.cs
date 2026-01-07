using System.Text.Json;
using Aevatar.Agents.A2UI.Events;
using Shouldly;

namespace Aevatar.Agents.A2UI.Tests;

public class A2UiEventsTests
{
    [Fact]
    public void EventTypeConstants_ShouldBeStable()
    {
        // A2UI specific events
        new SurfaceUpdateEvent { SurfaceId = "s", Template = "t" }.Type.ShouldBe("surfaceUpdate");
        new DataModelUpdateEvent { SurfaceId = "s", Data = new object() }.Type.ShouldBe("dataModelUpdate");
        new BeginRenderingEvent { SurfaceId = "s" }.Type.ShouldBe("beginRendering");
        new DeleteSurfaceEvent { SurfaceId = "s" }.Type.ShouldBe("deleteSurface");
        new A2UiUserActionEvent { SurfaceId = "s", ActionId = "a" }.Type.ShouldBe("A2uiUserAction");
    }

    [Fact]
    public void SurfaceUpdateEvent_ShouldSerialize_ToJson()
    {
        var evt = new SurfaceUpdateEvent
        {
            Timestamp = 123456789,
            SurfaceId = "main_dashboard",
            Template = "{\"type\": \"AdaptiveCard\"}",
            InitialData = new { count = 1 }
        };

        var json = JsonSerializer.Serialize(evt);
        json.ShouldContain("\"Type\"");
        json.ShouldContain("surfaceUpdate");
        json.ShouldContain("main_dashboard");
        json.ShouldContain("AdaptiveCard");
        json.ShouldContain("\"InitialData\"");
    }

    [Fact]
    public void UserActionEvent_ShouldSerialize_ToJson()
    {
        var evt = new A2UiUserActionEvent
        {
            Timestamp = 987654321,
            SurfaceId = "login_form",
            ActionId = "submit_btn",
            Payload = new { username = "admin" }
        };

        var json = JsonSerializer.Serialize(evt);
        json.ShouldContain("A2uiUserAction");
        json.ShouldContain("login_form");
        json.ShouldContain("submit_btn");
        json.ShouldContain("username");
        json.ShouldContain("admin");
    }
}

