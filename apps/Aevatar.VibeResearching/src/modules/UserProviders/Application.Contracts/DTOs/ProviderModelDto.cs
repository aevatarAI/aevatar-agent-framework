namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for a model available from a provider.
/// </summary>
public sealed record ProviderModelDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string OwnedBy { get; init; } = string.Empty;
}
