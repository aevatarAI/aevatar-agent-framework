namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for updating agent-provider mappings in a session.
/// </summary>
public sealed record UpdateAgentProvidersDto
{
    /// <summary>
    /// Map of agent name to provider namespace (e.g., "user:abc123" or "platform:openai-gpt4").
    /// Null values remove the mapping for that agent (falls back to resolution chain).
    /// </summary>
    public Dictionary<string, string?> Map { get; init; } = new();
}
