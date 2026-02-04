namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Request to set an API key for an LLM provider.
/// </summary>
public sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

/// <summary>
/// Request to set the default LLM provider.
/// </summary>
public sealed record SetLlmDefaultRequest(string? ProviderName);

/// <summary>
/// Request to create or update an LLM provider instance.
/// </summary>
public sealed record UpsertLlmInstanceRequest(
    string? ProviderName,
    string? ProviderType,
    string? Model,
    string? Endpoint,
    string? ApiKey,
    string? CopyApiKeyFrom);

/// <summary>
/// Request to probe/test an LLM endpoint.
/// </summary>
public sealed record ProbeLlmRequest(
    string? ProviderType,
    string? Endpoint,
    string? ApiKey);

/// <summary>
/// Request to configure embeddings provider.
/// </summary>
public sealed record UpsertEmbeddingsRequest(
    bool? Enabled,
    string? ProviderType,
    string? Model,
    string? Endpoint,
    string? ApiKey);

/// <summary>
/// Represents a trashed API key entry for recovery.
/// </summary>
public sealed record TrashedApiKeyEntry(
    string ProviderName,
    string ProviderType,
    string Model,
    string Endpoint,
    string OriginalKeyPath,
    long TrashedAtUnixMs,
    string ApiKey,
    Dictionary<string, string>? ProviderKeys = null);

/// <summary>
/// List item representing a trashed API key (without exposing the full key).
/// </summary>
public sealed record TrashedApiKeyListItem(
    string ProviderName,
    string ProviderType,
    string Model,
    string Endpoint,
    long TrashedAtUnixMs,
    string Masked);

/// <summary>
/// Request to set a generic secret value.
/// </summary>
public sealed record SetSecretRequest(string? Key, string? Value);

/// <summary>
/// Request to remove a secret.
/// </summary>
public sealed record RemoveSecretRequest(string? Key);

/// <summary>
/// LLM provider kind enumeration.
/// </summary>
public enum LlmProviderKind
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    Google = 2
}

/// <summary>
/// Provider type metadata for UI display.
/// </summary>
public sealed record ProviderTypeItem(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    bool Recommended,
    int ConfiguredInstancesCount,
    bool ApiKeyConfigured);

/// <summary>
/// Provider instance configuration.
/// </summary>
public sealed record ProviderInstanceItem(
    string Name,
    string ProviderType,
    string ProviderDisplayName,
    string Model,
    string Endpoint);

/// <summary>
/// Provider profile template.
/// </summary>
public sealed record ProviderProfile(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    LlmProviderKind Kind,
    string DefaultEndpoint,
    string DefaultModel,
    bool Recommended = false);

/// <summary>
/// Resolved provider information (public view, without API keys).
/// </summary>
public sealed record ResolvedProviderPublic(
    string ProviderName,
    string ProviderType,
    string ProviderTypeSource,
    string DisplayName,
    string Kind,
    bool ApiKeyConfigured,
    string Endpoint,
    string EndpointSource,
    string Model,
    string ModelSource);

/// <summary>
/// Resolved provider information (internal view, includes API key).
/// </summary>
public sealed record ResolvedProvider(
    string ProviderName,
    string ProviderType,
    string ProviderTypeSource,
    string DisplayName,
    LlmProviderKind Kind,
    string Endpoint,
    string EndpointSource,
    string Model,
    string ModelSource,
    bool ApiKeyConfigured,
    string ApiKey,
    ResolvedProviderPublic Public);
