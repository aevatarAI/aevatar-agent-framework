namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for creating a new user LLM provider.
/// </summary>
public sealed record CreateUserProviderDto
{
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public string? Endpoint { get; init; }
    public string DefaultModel { get; init; } = string.Empty;
    public string? DeploymentName { get; init; }
    public bool IsDefault { get; init; }
}
