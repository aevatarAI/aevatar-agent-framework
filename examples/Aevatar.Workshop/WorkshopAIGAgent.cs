using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Workshop.Messages;

namespace Aevatar.Workshop;

public class WorkshopAIGAgent : AIGAgentBase
{
    public WorkshopAIGAgent()
    {
    }

    public WorkshopAIGAgent(string id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("Aevatar.Workshop:WorkshopAIGAgent");

    // Public event handler (method-level)
    [EventHandler]
    public async Task HandlePingAsync(WorkshopPingEvent evt)
    {
        if (evt == null) return;

        var response = new WorkshopPongEvent
        {
            RequestId = evt.RequestId,
            Content = $"pong: {evt.Content}".Trim(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await PublishAsync(response, Aevatar.Agents.EventDirection.Down);
    }

    // Protected event handler (should still be discoverable by reflection)
    [EventHandler]
    protected Task ObservePingAsync(WorkshopPingEvent evt)
    {
        if (evt == null) return Task.CompletedTask;

        State.Context["last_ping_observed"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        return Task.CompletedTask;
    }
}
