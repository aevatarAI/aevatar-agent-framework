using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// Routing agent
public class RouterAgent : GAgentBase<RouterState>
{
    
    [EventHandler]
    public Task HandleRoutingMessage(RoutingMessage message)
    {
        State.MessagesRouted++;
        Logger?.LogInformation("Router {Id} routing message {MessageId} with routing info: {RoutingInfo}", 
            Id, message.Id, message.RoutingInfo);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Router {Id}: Routed {State.MessagesRouted} messages");
    }
}

// RouterState is defined in demo_messages.proto

// Processor agent
public class ProcessorAgent : GAgentBase<ProcessorState>
{
    
    [AllEventHandler]
    public Task ProcessEvent(EventEnvelope envelope)
    {
        State.MessagesProcessed++;
        State.LastProcessed = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        Logger?.LogInformation("Processor {Id} processed event #{Count}", Id, State.MessagesProcessed);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Processor {Id}: Processed {State.MessagesProcessed} events");
    }
}

// ProcessorState is defined in demo_messages.proto

// Filter agent
public class FilterAgent : GAgentBase<FilterState>
{
    
    [AllEventHandler]
    public Task FilterEvent(EventEnvelope envelope)
    {
        State.MessagesFiltered++;
        
        // Filter by priority (using Message field)
        if (envelope.Message?.Contains("high") == true)
        {
            State.MessagesPassed++;
            Logger?.LogInformation("Filter {Id} passing high priority message", Id);
            // Continue propagation
            return Task.CompletedTask;
        }
        else
        {
            State.MessagesFiltered++;
            Logger?.LogInformation("Filter {Id} filtered out low priority message", Id);
            // Stop propagation
            envelope.ShouldStopPropagation = true;
            return Task.CompletedTask;
        }
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Filter {Id}: {State.MessagesFiltered} filtered, {State.MessagesPassed} passed");
    }
}

// FilterState is defined in demo_messages.proto

// Logger agent
public class LoggerAgent : GAgentBase<LoggerState>
{
    
    [AllEventHandler]
    public Task LogEvent(EventEnvelope envelope)
    {
        State.MessagesLogged++;
        if (!string.IsNullOrEmpty(envelope.Message))
        {
            State.RecentLogs.Add(envelope.Message);
            if (State.RecentLogs.Count > 10) // Keep last 10 log entries
            {
                State.RecentLogs.RemoveAt(0);
            }
        }
        Logger?.LogInformation("Logger {Id} logged event: {Message}", Id, envelope.Message);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Logger {Id}: Logged {State.MessagesLogged} events");
    }
}

// LoggerState is defined in demo_messages.proto

// Broadcast agent
public class BroadcastAgent : GAgentBase<BroadcastState>
{
    
    [EventHandler]
    public Task HandleBroadcast(BroadcastMessage broadcast)
    {
        State.MessagesBroadcast++;
        State.ReceiverCount = 1; // Self is the receiver
        Logger?.LogInformation("BroadcastAgent {Id} received broadcast on topic {Topic}: {Content}", 
            Id, broadcast.Topic, broadcast.Content);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"BroadcastAgent {Id}: Broadcast {State.MessagesBroadcast} messages to {State.ReceiverCount} receivers");
    }
}

// BroadcastState is defined in demo_messages.proto
