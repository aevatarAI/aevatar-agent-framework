using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Persistence;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent - with state and configuration
/// </summary>
public class ConfigurableTestAgent : GAgentBase<TestAgentState, TestAgentConfig>
{
    public override string GetDescription()
    {
        return $"ConfigurableAgent: {Config.AgentName} (Retries: {Config.MaxRetries})";
    }

    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        // Initialize configuration
        Config.AgentName = "ConfigurableAgent";
        Config.MaxRetries =3;
        Config.TimeoutSeconds = 30;
        Config.EnableLogging = true;
 
        // Initialize state
        State.Name = Config.AgentName;
        State.Counter = 0;

        return base.OnActivateAsync(ct);
    }

    [EventHandler]
    public async Task HandleTestEventAsync(TestEvent evt)
    {
        if (Config.EnableLogging)
        {
            Logger.LogInformation("Handling event: {EventId}", evt.EventId);
        }

        State.Counter++;

        // Use retry logic from configuration
        for (var i = 0; i < Config.MaxRetries; i++)
        {
            try
            {
                // Simulate operation that might fail
                await ProcessEventWithRetry(evt);
                break;
            }
            catch when (i < Config.MaxRetries - 1)
            {
                await Task.Delay(100);
            }
        }
    }

    [EventHandler]
    public async Task ChangeStateAsync(Empty empty)
    {
        State.Name = "DualGeneric";
        State.Counter = 100;
    }

    private Task ProcessEventWithRetry(TestEvent evt)
    {
        // Simulate processing logic
        return Task.CompletedTask;
    }
}