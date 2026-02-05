namespace Aevatar.VibeResearching.UserProviders.Constants;

/// <summary>
/// Constants for the UserProviders module.
/// </summary>
public static class UserProviderConsts
{
    /// <summary>Maximum number of LLM providers a single user can configure.</summary>
    public const int MaxProvidersPerUser = 20;

    /// <summary>Maximum length for the user-facing provider name.</summary>
    public const int MaxProviderNameLength = 100;

    /// <summary>Maximum length for an API key.</summary>
    public const int MaxApiKeyLength = 500;

    /// <summary>Maximum length for a model identifier.</summary>
    public const int MaxModelLength = 200;

    /// <summary>Maximum length for an Azure deployment name.</summary>
    public const int MaxDeploymentNameLength = 200;

    /// <summary>Maximum length for a custom endpoint URL.</summary>
    public const int MaxEndpointLength = 2000;

    /// <summary>Regex for valid provider names (alphanumeric, spaces, hyphens, underscores).</summary>
    public const string ProviderNamePattern = @"^[a-zA-Z0-9 _-]+$";

    /// <summary>Auto-created name for the Codex OAuth provider.</summary>
    public const string CodexProviderName = "Codex (ChatGPT)";

    /// <summary>Default model assigned to new Codex providers.</summary>
    public const string CodexDefaultModel = "gpt-5.2-codex";

    /// <summary>Models supported by Codex OAuth (no dynamic API — hardcoded from opencode/Cline).</summary>
    public static readonly string[] CodexSupportedModels =
    {
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1-codex-max",
        "gpt-5.1-codex",
        "gpt-5.1-codex-mini"
    };

    /// <summary>MongoDB collection name for user LLM providers.</summary>
    public const string ProvidersCollectionName = "user_llm_providers";

    /// <summary>MongoDB collection name for user Codex tokens.</summary>
    public const string CodexTokensCollectionName = "user_codex_tokens";

    /// <summary>Environment variable name for the encryption master key.</summary>
    public const string MasterKeyEnvVar = "AEVATAR_USER_PROVIDER_MASTER_KEY";

    /// <summary>Additional authenticated data for AES-256-GCM encryption.</summary>
    public const string EncryptionAad = "aevatar-user-provider-v1";

    /// <summary>All supported provider type strings.</summary>
    public static readonly string[] SupportedProviderTypes =
    {
        "OpenAI", "AzureOpenAI", "Anthropic", "Google",
        "Ollama", "OpenRouter", "DeepSeek", "CodexOAuth"
    };
}
