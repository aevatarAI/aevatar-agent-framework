namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for the Codex OAuth callback result.
/// </summary>
public sealed record CodexCallbackResultDto
{
    public string Status { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? ProviderId { get; init; }
}
