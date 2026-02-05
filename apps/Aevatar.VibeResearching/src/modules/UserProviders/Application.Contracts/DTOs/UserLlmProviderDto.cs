namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for a user LLM provider. API key is always masked.
/// </summary>
public sealed record UserLlmProviderDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string? Endpoint { get; init; }
    public string DefaultModel { get; init; } = string.Empty;
    public string? DeploymentName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCodexOAuth { get; init; }
    public string MaskedApiKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
