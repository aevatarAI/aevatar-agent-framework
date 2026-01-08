using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Demo.Agents;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Agent demonstrating observability features
/// Automatically records metrics and uses structured logging
/// </summary>
public class ObservableAgent : GAgentBase<SimpleAgentState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Observable Agent with built-in metrics and logging");
    }

    /// <summary>
    /// Handle weather update event
    /// </summary>
    [EventHandler]
    public async Task HandleWeatherUpdate(WeatherUpdateEvent evt)
    {
        // Logs already contain structured context information (AgentId, EventId, EventType, etc.)
        Logger.LogInformation("Processing weather update for {Location}", evt.Location);
        
        State.Name = $"Weather in {evt.Location}";
        State.Counter++;
        State.IsActive = true;
        
        // Simulate some processing time
        await Task.Delay(Random.Shared.Next(10, 50));
        
        // Publish response event (will automatically record publishing metrics)
        var response = new BroadcastMessage
        {
            Id = Guid.NewGuid().ToString(),
            Topic = "weather.processed",
            Content = $"Temperature in {evt.Location}: {evt.Temperature}°C",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await PublishAsync(response, EventDirection.Up);
        
        Logger.LogInformation("Weather update processed successfully");
    }

    /// <summary>
    /// Handle broadcast message
    /// </summary>
    [EventHandler]
    public Task HandleBroadcast(BroadcastMessage msg)
    {
        Logger.LogDebug("Received broadcast on topic {Topic}: {Content}", 
            msg.Topic, msg.Content);
        
        State.Items.Add($"{msg.Topic}: {msg.Content}");
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle all events (demonstrating AllEventHandler)
    /// </summary>
    [AllEventHandler]
    public Task HandleAllEvents(EventEnvelope envelope)
    {
        // This handler will log all passing events
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";
        
        Logger.LogTrace("Event flow: {EventId} of type {EventType} from {PublisherId}", 
            envelope.Id, eventType, envelope.PublisherId);
        
        State.Attributes[$"last_event_{eventType}"] = envelope.Id;
        
        return Task.CompletedTask;
    }

    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("ObservableAgent {Id} activated", Id);
        
        // Initialize state
        State.Name = "Observable Agent";
        State.IsActive = true;
        
        await base.OnActivateAsync(ct);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("ObservableAgent {Id} deactivated with {Count} events processed", 
            Id, State.Counter);
        
        State.IsActive = false;
        
        await base.OnDeactivateAsync(ct);
    }
}

/// <summary>
/// Observability features explanation
/// </summary>
public static class ObservabilityFeatures
{
    public static void Describe()
    {
        Console.WriteLine(@"
=== Observability Features ===
 
The framework now includes built-in observability features:
 
1. **Automatic Metrics Collection**
    - Event publish count and latency
    - Event processing count and latency
    - Exception count (classified by type and operation)
    - Active Actor count
    - Event drop count
 
2. **Structured Logging**
    - Automatically adds context information (AgentId, EventId, EventType, etc.)
    - Supports logging scopes for easier operation chain tracking
    - Event processing and publishing automatically include relevant metadata
 
3. **Performance Monitoring**
    - Uses System.Diagnostics.Metrics API
    - Compatible with OpenTelemetry
    - Supports monitoring systems like Prometheus, Application Insights, etc.
 
4. **Usage**
    All features are automatic, no additional code needed:
    - Automatically record metrics when publishing events
    - Automatically create logging scopes when processing events
    - Actor lifecycle automatically updates counters
    - Exceptions automatically recorded and classified
 
5. **Integration Points**
    - GAgentBase: event publishing and processing
    - GAgentActorBase: Actor-level event routing
    - LocalGAgentActor/ProtoActorGAgentActor: active Actor counting
 
6. **Export Metrics**
    Can export metrics using OpenTelemetry:
    ```csharp
    services.AddOpenTelemetry()
        .WithMetrics(builder => builder
            .AddMeter(""Aevatar.Agents"")
            .AddPrometheusExporter());
    ```
 
These features enable developers to:
- Monitor system performance in real-time
- Quickly locate issues
- Analyze event flow patterns
- Optimize system performance
 ");
    }
}
