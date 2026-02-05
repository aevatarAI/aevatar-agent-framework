namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;

/// <summary>
/// Configuration options for the Codex (ChatGPT) OAuth flow.
/// </summary>
public sealed class CodexOAuthOptions
{
    public string ClientId { get; set; } = "app_EMoamEEZ73f0CkXaXp7hrann";
    public string Issuer { get; set; } = "https://auth.openai.com";
    public string AuthorizationEndpoint { get; set; } = "https://auth.openai.com/oauth/authorize";
    public string TokenEndpoint { get; set; } = "https://auth.openai.com/oauth/token";
    public string Scopes { get; set; } = "openid profile email offline_access";
    public string CodexApiBaseUrl { get; set; } = "https://chatgpt.com/backend-api/codex/responses";

    /// <summary>Port for the local OAuth callback listener.</summary>
    public int CallbackPort { get; set; } = 1455;

    /// <summary>Path the callback listener handles.</summary>
    public string CallbackPath { get; set; } = "/auth/callback";

    /// <summary>Frontend origin to redirect to after callback completes.</summary>
    public string FrontendOrigin { get; set; } = "http://localhost:5173";
}
