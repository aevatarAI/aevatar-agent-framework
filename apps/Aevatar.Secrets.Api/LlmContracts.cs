// ============================================================
//  LLM Contracts (Secrets API)
//
//  中文说明：
//  - 这些类型是 Secrets API 的“UI DTO / 内部模型”
//  - 不跨服务边界（不受 Protobuf 铁律约束）
// ============================================================

sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

sealed record SetLlmDefaultRequest(string? ProviderName);

sealed record UpsertLlmInstanceRequest(
    string? ProviderName,
    string? ProviderType,
    string? Model,
    string? Endpoint,
    string? ApiKey,
    string? CopyApiKeyFrom);

sealed record ProbeLlmRequest(
    string? ProviderType,
    string? Endpoint,
    string? ApiKey);

sealed record UpsertEmbeddingsRequest(
    bool? Enabled,
    string? ProviderType,
    string? Model,
    string? Endpoint,
    string? ApiKey);

sealed record UpsertSkillsMpRequest(
    string? ApiKey,
    string? BaseUrl);

sealed record TrashedApiKeyEntry(
    string ProviderName,
    string ProviderType,
    string Model,
    string Endpoint,
    string OriginalKeyPath,
    long TrashedAtUnixMs,
    string ApiKey,
    Dictionary<string, string>? ProviderKeys = null);

sealed record TrashedApiKeyListItem(
    string ProviderName,
    string ProviderType,
    string Model,
    string Endpoint,
    long TrashedAtUnixMs,
    string Masked);

sealed record SetSecretRequest(string? Key, string? Value);

sealed record RemoveSecretRequest(string? Key);

enum LlmProviderKind
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    Google = 2
}

sealed record ProviderTypeItem(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    bool Recommended,
    int ConfiguredInstancesCount);

sealed record ProviderInstanceItem(
    string Name,
    string ProviderType,
    string ProviderDisplayName,
    string Model,
    string Endpoint);

sealed record ProviderProfile(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    LlmProviderKind Kind,
    string DefaultEndpoint,
    string DefaultModel,
    bool Recommended = false);

sealed record ResolvedProviderPublic(
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

sealed record ResolvedProvider(
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

static class SecretMask
{
    public static string MaskMiddle(string raw, int prefix = 4, int suffix = 4)
    {
        var s = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        prefix = Math.Clamp(prefix, 0, 16);
        suffix = Math.Clamp(suffix, 0, 16);

        // If too short, mask all but keep length stable.
        if (s.Length <= prefix + suffix || s.Length < 8)
            return new string('*', s.Length);

        var mid = s.Length - prefix - suffix;
        if (mid <= 0)
            return new string('*', s.Length);

        return s.Substring(0, prefix) + new string('*', mid) + s.Substring(s.Length - suffix, suffix);
    }
}


