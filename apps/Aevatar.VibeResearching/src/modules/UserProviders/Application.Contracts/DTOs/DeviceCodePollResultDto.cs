namespace Aevatar.VibeResearching.UserProviders.DTOs;

/// <summary>
/// Result of polling the device code token endpoint.
/// </summary>
public sealed class DeviceCodePollResultDto
{
    /// <summary>
    /// "pending" — user hasn't authorized yet.
    /// "connected" — tokens received, provider created.
    /// "expired" — device code expired, restart flow.
    /// "error" — unexpected error.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Email from the ID token (set on "connected").</summary>
    public string? Email { get; set; }

    /// <summary>Auto-created Codex provider ID (set on "connected").</summary>
    public string? ProviderId { get; set; }
}
