namespace Aevatar.VibeResearching.Agents.Application.Contracts.DTOs;

/// <summary>
/// Input DTO for mapping an agent to a specific LLM provider.
/// </summary>
public sealed class AgentProviderDto
{
    /// <summary>
    /// Agent name/identifier (required).
    /// </summary>
    public string? Agent { get; init; }

    /// <summary>
    /// Agent name/identifier (alias for Agent property).
    /// </summary>
    public string? AgentName => Agent;

    /// <summary>
    /// Provider name to use for this agent.
    /// Empty or "default" clears the mapping.
    /// </summary>
    public string? ProviderName { get; init; }
}
