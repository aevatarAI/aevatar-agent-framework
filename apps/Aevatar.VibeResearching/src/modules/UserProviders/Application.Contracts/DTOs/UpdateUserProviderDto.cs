namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for updating an existing user LLM provider.
/// Null fields retain existing values.
/// </summary>
public sealed record UpdateUserProviderDto
{
    public string? Name { get; init; }
    public string? ProviderType { get; init; }
    /// <summary>Null means retain existing key. Codex OAuth providers ignore this field.</summary>
    public string? ApiKey { get; init; }
    public string? Endpoint { get; init; }
    public string? DefaultModel { get; init; }
    public string? DeploymentName { get; init; }
}
