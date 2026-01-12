using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Complex state agent
/// </summary>
public class ComplexAgent : GAgentBase<ComplexAgentState>
{
    public override string GetDescription()
    {
        return $"ComplexAgent: {State.AgentId} (Status: {State.Status}, Events: {State.EventCount})";
    }

    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.AgentId = Guid.NewGuid().ToString();
        State.Status = ComplexAgentState.Types.Status.Active;
        State.Nested = new ComplexAgentState.Types.NestedState
        {
            SubId = "nested-1",
            SubCounter = 0,
            SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        State.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        return base.OnActivateAsync(ct);
    }

    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        State.Nested.SubCounter++;
        State.NestedList.Add(new ComplexAgentState.Types.NestedState
        {
            SubId = evt.EventId,
            SubCounter = State.Nested.SubCounter,
            SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        State.EventCount++;
        State.LastEventId = evt.EventId;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleComplexEvent(EventEnvelope envelope)
    {
        // Extract the complex event
        if (envelope.Payload != null && envelope.Payload.Is(ComplexTestEvent.Descriptor))
        {
            var evt = envelope.Payload.Unpack<ComplexTestEvent>();
            
            State.EventCount++;
            State.LastEventId = evt.Id;
            State.LastUpdated = evt.Timestamp ?? Timestamp.FromDateTime(DateTime.UtcNow);
            
            // Process nested events
            foreach (var nested in evt.NestedEvents)
            {
                State.NestedList.Add(new ComplexAgentState.Types.NestedState
                {
                    SubId = nested.NestedId,
                    SubCounter = nested.NestedValue,
                    SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }
            
            // Process data map
            foreach (var kvp in evt.Data)
            {
                State.DataMap[kvp.Key] = kvp.Value;
            }
        }
        
        await Task.CompletedTask;
    }
}