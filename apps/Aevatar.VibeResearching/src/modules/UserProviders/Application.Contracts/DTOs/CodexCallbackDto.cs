namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Request DTO for the Codex OAuth callback.
/// </summary>
public sealed record CodexCallbackDto
{
    public string Code { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
}
