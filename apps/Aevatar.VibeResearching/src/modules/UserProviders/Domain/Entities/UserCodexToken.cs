namespace Aevatar.VibeResearching.UserProviders.Entities;

/// <summary>
/// Stores encrypted Codex OAuth tokens for a user.
/// One record per user (unique on UserId).
/// MongoDB collection: 'user_codex_tokens'.
/// </summary>
public sealed class UserCodexToken
{
    /// <summary>MongoDB ObjectId as string.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ABP Identity user ID (unique index).</summary>
    public Guid UserId { get; set; }

    /// <summary>AES-256-GCM encrypted access token.</summary>
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>AES-256-GCM encrypted refresh token.</summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    /// <summary>When the access token expires.</summary>
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>ChatGPT account ID for the chatgpt-account-id header.</summary>
    public string? ChatGptAccountId { get; set; }

    /// <summary>OpenAI account email (display-only).</summary>
    public string? Email { get; set; }

    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
