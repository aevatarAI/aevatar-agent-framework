using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Basic test agent - with state
/// </summary>
public class BasicTestAgent : GAgentBase<TestAgentState>
{
    public bool OnActivateCalled { get; private set; }
    public bool OnDeactivateCalled { get; private set; }
    public int HandleEventCallCount { get; private set; }
    
    public override string GetDescription()
    {
        return $"TestAgent: {State.Name} (Counter: {State.Counter})";
    }
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        OnActivateCalled = true;
        State.Name = "TestAgent";
        State.Counter = 0;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnActivateAsync(ct);
    }

    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        OnDeactivateCalled = true;
        return base.OnDeactivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        HandleEventCallCount++;
        State.Counter++;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleTestCommand(TestCommand cmd)
    {
        State.Counter += 10;
        if (cmd.Parameters.TryGetValue("name", out var parameter))
        {
            State.Name = parameter;
        }
        await Task.CompletedTask;
    }

    [EventHandler]
    public async Task ModifyStateAsync(Empty empty)
    {
        State.Counter = 42;
        State.Name = "SingleGeneric";
    }
    
    [AllEventHandler]
    public async Task HandleAnyEvent(EventEnvelope envelope)
    {
        State.Items.Add($"Event: {envelope.Id}");
        await Task.CompletedTask;
    }
}