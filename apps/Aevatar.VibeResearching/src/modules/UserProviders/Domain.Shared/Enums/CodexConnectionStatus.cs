namespace Aevatar.VibeResearching.UserProviders.Enums;

/// <summary>
/// Status of a user's Codex OAuth connection.
/// </summary>
public enum CodexConnectionStatus
{
    /// <summary>No Codex connection exists for this user.</summary>
    Disconnected = 0,

    /// <summary>Codex OAuth flow is in progress (state issued, awaiting callback).</summary>
    Pending = 1,

    /// <summary>Codex is connected and tokens are valid.</summary>
    Connected = 2,

    /// <summary>Token refresh failed; user needs to re-authenticate.</summary>
    Expired = 3
}
