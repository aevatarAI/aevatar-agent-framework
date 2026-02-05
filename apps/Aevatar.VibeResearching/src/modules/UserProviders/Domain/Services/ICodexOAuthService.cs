namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Manages the Codex OAuth PKCE flow lifecycle.
/// </summary>
public interface ICodexOAuthService
{
    /// <summary>
    /// Generates PKCE parameters and returns the authorization URL.
    /// </summary>
    Task<CodexInitiateResult> InitiateAsync(
        Guid userId,
        string redirectUri,
        CancellationToken ct = default);

    /// <summary>
    /// Exchanges the authorization code for tokens.
    /// Encrypts and stores tokens, auto-creates/updates the Codex provider.
    /// </summary>
    Task<CodexCallbackResult> HandleCallbackAsync(
        Guid userId,
        string code,
        string state,
        string redirectUri,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current Codex connection status for a user.
    /// </summary>
    Task<CodexConnectionInfo> GetStatusAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Disconnects Codex by deleting tokens and the auto-created provider.
    /// </summary>
    Task DisconnectAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a valid (refreshed if needed) access token for Codex API calls.
    /// </summary>
    Task<string> GetValidAccessTokenAsync(
        Guid userId,
        CancellationToken ct = default);
}

/// <summary>Result of initiating the Codex OAuth flow.</summary>
public sealed record CodexInitiateResult(string AuthUrl, string State);

/// <summary>Result of handling the Codex OAuth callback.</summary>
public sealed record CodexCallbackResult(string Status, string? Email, string? ProviderId);

/// <summary>Current Codex connection info for a user.</summary>
public sealed record CodexConnectionInfo(bool Connected, string? Email, DateTimeOffset? ConnectedAt, string? ProviderId);
