namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Detailed information about a provider mapped to an agent.
/// </summary>
public sealed record AgentProviderDetailDto
{
    public string Namespace { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}
