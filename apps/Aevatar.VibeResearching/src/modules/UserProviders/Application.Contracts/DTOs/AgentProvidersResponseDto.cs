namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for agent-provider mapping state in a session.
/// </summary>
public sealed record AgentProvidersResponseDto
{
    /// <summary>Snapshot version number for optimistic concurrency.</summary>
    public int Version { get; init; }

    /// <summary>ISO 8601 timestamp of last update.</summary>
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>Map of agent name to provider detail.</summary>
    public Dictionary<string, AgentProviderDetailDto> Map { get; init; } = new();
}
