using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Actor example demonstrating automatic Logger injection
/// </summary>
public class SimpleAutoLoggerActor : GAgentActorBase
{
    /// <summary>
    /// Constructor using only Agent parameters - supports automatic Logger injection
    /// </summary>
    public SimpleAutoLoggerActor(IGAgent agent)
        : base(agent)
    {
        // Logger will be automatically injected, no need to pass it manually
    }

    protected override Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Use auto-injected Logger
        Logger.LogInformation("Actor {ActorId} sending event {EventId} to self", 
            Id, envelope.Id);
        
        // Actual sending logic (simplified example)
        return HandleEventAsync(envelope, ct);
    }

    protected override Task SendEventToActorAsync(string actorId, EventEnvelope envelope, CancellationToken ct)
    {
        // Use auto-injected Logger
        Logger.LogInformation("Actor {ActorId} sending event {EventId} to actor {TargetActorId}", 
            Id, envelope.Id, actorId);
        
        // In actual implementation, this would send event to target Actor via some mechanism
        return Task.CompletedTask;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Use auto-injected Logger
        Logger.LogInformation("Actor {ActorId} activated with agent type {AgentType}", 
            Id, Agent.GetType().Name);
        
        await Task.CompletedTask;
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Use auto-injected Logger
        Logger.LogInformation("Actor {ActorId} deactivated", Id);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Override event handling, add logging
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        Logger.LogDebug("Actor {ActorId} received event {EventId} from {PublisherId}", 
            Id, envelope.Id, envelope.PublisherId);
        
        await base.HandleEventAsync(envelope, ct);
    }
}
