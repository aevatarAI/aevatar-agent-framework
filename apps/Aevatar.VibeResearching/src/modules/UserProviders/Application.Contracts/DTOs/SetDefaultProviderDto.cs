namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for setting the default provider.
/// </summary>
public sealed record SetDefaultProviderDto
{
    public string ProviderId { get; init; } = string.Empty;
}
