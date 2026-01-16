namespace VibeResearching.Api.Infrastructure;

public static partial class LlmSecretsApi
{
    // ------------------------------------------------------------
    // Contracts (simple JSON records)
    // ------------------------------------------------------------
    private sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

    private sealed record SetLlmDefaultRequest(string? ProviderName);

    private sealed record UpsertLlmInstanceRequest(
        string? ProviderName,
        string? ProviderType,
        string? Model,
        string? Endpoint,
        string? ApiKey,
        string? CopyApiKeyFrom);

    private sealed record ProbeLlmRequest(
        string? ProviderType,
        string? Endpoint,
        string? ApiKey);

    private sealed record UpsertEmbeddingsRequest(
        bool? Enabled,
        string? ProviderType,
        string? Model,
        string? Endpoint,
        string? ApiKey);


    private sealed record TrashedApiKeyEntry(
        string ProviderName,
        string ProviderType,
        string Model,
        string Endpoint,
        string OriginalKeyPath,
        long TrashedAtUnixMs,
        string ApiKey,
        Dictionary<string, string>? ProviderKeys = null);

    private sealed record TrashedApiKeyListItem(
        string ProviderName,
        string ProviderType,
        string Model,
        string Endpoint,
        long TrashedAtUnixMs,
        string Masked);

    private sealed record SetSecretRequest(string? Key, string? Value);
    private sealed record RemoveSecretRequest(string? Key);

    // ------------------------------------------------------------
    // Provider model (UI + resolver)
    // ------------------------------------------------------------
    private enum LlmProviderKind
    {
        OpenAiCompatible = 0,
        Anthropic = 1,
        Google = 2
    }

    private sealed record ProviderTypeItem(
        string Id,
        string DisplayName,
        string Category,
        string Description,
        bool Recommended,
        int ConfiguredInstancesCount);

    private sealed record ProviderInstanceItem(
        string Name,
        string ProviderType,
        string ProviderDisplayName,
        string Model,
        string Endpoint);

    private sealed record ProviderProfile(
        string Id,
        string DisplayName,
        string Category,
        string Description,
        LlmProviderKind Kind,
        string DefaultEndpoint,
        string DefaultModel,
        bool Recommended = false);

    private sealed record ResolvedProviderPublic(
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

    private sealed record ResolvedProvider(
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

    // ------------------------------------------------------------
    // Mask helper (UI only)
    // ------------------------------------------------------------
    private static class SecretMask
    {
        public static string MaskMiddle(string raw, int prefix = 4, int suffix = 4)
        {
            var s = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            prefix = Math.Clamp(prefix, 0, 16);
            suffix = Math.Clamp(suffix, 0, 16);

            if (s.Length <= prefix + suffix || s.Length < 8)
                return new string('*', s.Length);

            var mid = s.Length - prefix - suffix;
            if (mid <= 0)
                return new string('*', s.Length);

            return s.Substring(0, prefix) + new string('*', mid) + s.Substring(s.Length - suffix, suffix);
        }
    }
}


