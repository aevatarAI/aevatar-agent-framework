namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for initiating the Codex OAuth flow.
/// </summary>
public sealed record CodexInitiateResultDto
{
    public string AuthUrl { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}
