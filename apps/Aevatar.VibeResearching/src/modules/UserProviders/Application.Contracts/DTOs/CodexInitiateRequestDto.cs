namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for initiating the Codex OAuth flow.
/// </summary>
public sealed record CodexInitiateRequestDto
{
    public string RedirectUri { get; init; } = string.Empty;
}
