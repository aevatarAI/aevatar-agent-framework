namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for an available provider (user or platform).
/// </summary>
public sealed record AvailableProviderDto
{
    public string Namespace { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ProviderType { get; init; } = string.Empty;
    public string DefaultModel { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
