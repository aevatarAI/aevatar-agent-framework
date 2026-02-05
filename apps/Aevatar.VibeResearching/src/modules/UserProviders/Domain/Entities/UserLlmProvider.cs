namespace Aevatar.VibeResearching.UserProviders.Entities;

/// <summary>
/// Represents a user-configured LLM provider with encrypted API key.
/// Stored in MongoDB collection 'user_llm_providers'.
/// </summary>
public sealed class UserLlmProvider
{
    /// <summary>MongoDB ObjectId as string.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ABP Identity user ID.</summary>
    public Guid UserId { get; set; }

    /// <summary>User-facing display name (e.g., "My OpenAI").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Provider type: OpenAI, AzureOpenAI, Anthropic, etc.</summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM encrypted API key, stored as JSON envelope string.
    /// Empty for Codex OAuth providers (tokens stored separately).
    /// </summary>
    public string EncryptedApiKey { get; set; } = string.Empty;

    /// <summary>Custom endpoint URL (nullable).</summary>
    public string? Endpoint { get; set; }

    /// <summary>Default model name (e.g., "gpt-4o").</summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>Azure-specific deployment name.</summary>
    public string? DeploymentName { get; set; }

    /// <summary>Whether this is the user's default provider.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether this provider uses Codex OAuth (token-based, not API key).</summary>
    public bool IsCodexOAuth { get; set; }

    /// <summary>Extensible metadata dictionary.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
