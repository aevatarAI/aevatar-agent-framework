namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;

/// <summary>
/// Determines how the user authenticates with Codex (ChatGPT).
/// </summary>
public enum CodexAuthMode
{
    /// <summary>Browser redirect to localhost callback (dev / local tools).</summary>
    Localhost,

    /// <summary>RFC 8628 device code grant (works from any environment including K8s).</summary>
    DeviceCode
}

/// <summary>
/// Configuration options for the Codex (ChatGPT) OAuth flow.
/// </summary>
public sealed class CodexOAuthOptions
{
    /// <summary>Which auth flow to use. Set via config/env.</summary>
    public CodexAuthMode AuthMode { get; set; } = CodexAuthMode.Localhost;

    public string ClientId { get; set; } = "app_EMoamEEZ73f0CkXaXp7hrann";
    public string Issuer { get; set; } = "https://auth.openai.com";
    public string AuthorizationEndpoint { get; set; } = "https://auth.openai.com/oauth/authorize";
    public string TokenEndpoint { get; set; } = "https://auth.openai.com/oauth/token";
    public string Scopes { get; set; } = "openid profile email offline_access";
    public string CodexApiBaseUrl { get; set; } = "https://chatgpt.com/backend-api/codex/responses";

    // --- Localhost mode ---

    /// <summary>Port for the local OAuth callback listener.</summary>
    public int CallbackPort { get; set; } = 1455;

    /// <summary>Path the callback listener handles.</summary>
    public string CallbackPath { get; set; } = "/auth/callback";

    /// <summary>Frontend origin to redirect to after callback completes.</summary>
    public string FrontendOrigin { get; set; } = "http://localhost:5173";

    // --- Device Code mode ---

    /// <summary>Endpoint to request a device auth session (custom OpenAI flow, NOT RFC 8628).</summary>
    public string DeviceUserCodeEndpoint { get; set; } = "https://auth.openai.com/api/accounts/deviceauth/usercode";

    /// <summary>Endpoint to poll for authorization code after user enters the code.</summary>
    public string DevicePollEndpoint { get; set; } = "https://auth.openai.com/api/accounts/deviceauth/token";

    /// <summary>URL shown to the user where they enter the code.</summary>
    public string DeviceVerificationUri { get; set; } = "https://auth.openai.com/codex/device";
}
