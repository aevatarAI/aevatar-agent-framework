namespace SisyphusMaker.Services;

/// <summary>
/// Configuration for the NyxID LLM Gateway, bound from appsettings "NyxGateway" section.
/// When enabled and a delegation token is present, LLM requests are routed through the gateway
/// instead of direct provider connections.
/// </summary>
public sealed class NyxGatewayOptions
{
    /// <summary>
    /// Full OpenAI-compatible gateway endpoint URL.
    /// Example: "https://nyx-api.chrono-ai.fun/api/v1/llm/openai-codex/v1"
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Fallback model for gateway routing when no model override is specified.</summary>
    public string DefaultModel { get; set; } = "gpt-5.2";

    /// <summary>Provider type for the gateway. Always "OpenAI" since the gateway is OpenAI-compatible.</summary>
    public string ProviderType { get; set; } = "OpenAI";

    /// <summary>Kill switch to disable NyxID gateway integration entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Returns the gateway endpoint URL. BaseUrl is used as-is (already the full endpoint).
    /// </summary>
    public string GetGatewayEndpoint() => BaseUrl.TrimEnd('/');
}
