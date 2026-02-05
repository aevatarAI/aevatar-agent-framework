namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Response DTO for Codex connection status.
/// </summary>
public sealed record CodexStatusDto
{
    public bool Connected { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public string? ProviderId { get; init; }
}
