using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Simple agent example - demonstrates automatic Logger injection
/// No need to handle Logger in constructor
/// </summary>
public class SimpleAutoLoggerAgent : GAgentBase<SimpleAgentState>
{
    private int _processedCount = 0;
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Simple Agent with auto-injected logger. Processed: {_processedCount} events");
    }
    
    [EventHandler]
    public Task HandleWeatherUpdate(WeatherUpdateEvent evt)
    {
        _processedCount++;
        
        // Logger has been automatically injected and can be used directly
        Logger.LogInformation("Received weather update: Temp={Temperature}, Condition={Condition}", 
            evt.Temperature, evt.Condition);
        
        State.Counter++;
        State.Attributes["temperature"] = evt.Temperature.ToString();
        State.Attributes["condition"] = evt.Condition;
        
        return Task.CompletedTask;
    }
    
    [EventHandler(Priority = 1)]
    public async Task HandleBroadcast(BroadcastMessage evt)
    {
        _processedCount++;
        
        Logger.LogDebug("Processing broadcast message: {Content} on topic {Topic}", 
            evt.Content, evt.Topic);
        
        State.Items.Add($"Broadcast-{evt.Content}");
        
        // 发布响应事件
        if (EventPublisher != null)
        {
            var response = new RoutingMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Processed: {evt.Content}",
                RoutingInfo = "processed",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            await PublishAsync(response, EventDirection.Up);
            Logger.LogInformation("Published routing message");
        }
    }
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize state
        State.Name = $"SimpleAutoLoggerAgent-{Id}";
        State.IsActive = true;
        
        // Logger is available here
        Logger.LogInformation("SimpleAutoLoggerAgent {Id} activated", Id);
    }
    
    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("SimpleAutoLoggerAgent {Id} deactivated after processing {Count} events", 
            Id, _processedCount);
        return base.OnDeactivateAsync(ct);
    }
}
