namespace ScientificResearchAssistant.Api.Infrastructure;

public static partial class LlmSecretsApi
{
    // ------------------------------------------------------------
    // Contracts (simple JSON records)
    // ------------------------------------------------------------
    private sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

    private sealed record UpsertLlmInstanceRequest(
        string? ProviderName,
        string? ProviderType,
        string? Model,
        string? Endpoint,
        string? ApiKey,
        string? CopyApiKeyFrom);

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
        public static string MaskMiddle(string s)
        {
            s ??= string.Empty;
            s = s.Trim();
            if (s.Length <= 6) return s;
            var keep = Math.Min(4, s.Length / 3);
            var head = s[..keep];
            var tail = s[^keep..];
            return head + new string('•', Math.Max(4, s.Length - keep * 2)) + tail;
        }
    }
}


