using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace AgentContextDemo;

/// <summary>
/// Agent that demonstrates context-aware event handling.
/// Shows how to access and use AgentContext within event handlers.
/// </summary>
public class ContextAwareAgent : GAgentBase<ContextAwareAgentState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Context-Aware Agent: {State.AgentName} (Events: {State.EventCount})");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentName = $"Agent_{Id:N}"[..14];
    }

    /// <summary>
    /// Event handler that accesses context values.
    /// </summary>
    [EventHandler]
    public async Task HandleProcessWithContext(ProcessWithContextEvent evt)
    {
        State.EventCount++;

        // Access context values using the Context property
        var correlationId = Context?.Get(AgentContextKeys.CorrelationId) ?? "N/A";
        var userId = Context?.Get(AgentContextKeys.UserId) ?? "N/A";
        var language = Context?.Get(AgentContextKeys.Language) ?? "N/A";
        var isCN = Context?.Get(AgentContextKeys.IsCN) ?? false;

        // Store received context for verification
        State.LastCorrelationId = correlationId;
        State.LastUserId = userId;
        State.LastLanguage = language;

        var contextLog = $"[{evt.RequestId}] CorrelationId={correlationId}, UserId={userId}, Language={language}, IsCN={isCN}";
        State.ReceivedContexts.Add(contextLog);

        Logger.LogInformation("🎯 [{AgentName}] Processing event with context: {ContextLog}", 
            State.AgentName, contextLog);

        // Publish response with context info
        await PublishAsync(new ContextProcessedEvent
        {
            RequestId = evt.RequestId,
            CorrelationId = correlationId,
            UserId = userId,
            Language = language,
            IsCn = isCN,
            ProcessedBy = State.AgentName
        }, Aevatar.Agents.EventDirection.Up);
    }

    /// <summary>
    /// Event handler for parent-child propagation test.
    /// The context should automatically propagate to child agents.
    /// </summary>
    [EventHandler]
    public async Task HandlePropagateContext(PropagateContextEvent evt)
    {
        State.EventCount++;

        var correlationId = Context?.Get(AgentContextKeys.CorrelationId) ?? "N/A";
        var userId = Context?.Get(AgentContextKeys.UserId) ?? "N/A";
        var language = Context?.Get(AgentContextKeys.Language) ?? "N/A";

        Logger.LogInformation("📨 [{AgentName}] Received propagation event. Context: CorrelationId={CorrelationId}, UserId={UserId}",
            State.AgentName, correlationId, userId);

        // Publish response to parent
        await PublishAsync(new ChildContextReceivedEvent
        {
            RequestId = evt.RequestId,
            ChildAgentId = State.AgentName,
            ReceivedCorrelationId = correlationId,
            ReceivedUserId = userId,
            ReceivedLanguage = language
        }, Aevatar.Agents.EventDirection.Up);
    }

    /// <summary>
    /// Gets all received context logs for verification.
    /// </summary>
    public IReadOnlyList<string> GetReceivedContexts() => State.ReceivedContexts;

    /// <summary>
    /// Gets the last received context values.
    /// </summary>
    public (string CorrelationId, string UserId, string Language) GetLastContext()
    {
        return (State.LastCorrelationId, State.LastUserId, State.LastLanguage);
    }
}
