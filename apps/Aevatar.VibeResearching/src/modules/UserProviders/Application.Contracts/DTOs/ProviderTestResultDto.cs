namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for a provider connectivity test.
/// </summary>
public sealed record ProviderTestResultDto
{
    public bool Ok { get; init; }
    public long LatencyMs { get; init; }
    public string Model { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
