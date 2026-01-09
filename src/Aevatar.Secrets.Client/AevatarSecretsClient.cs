using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.Secrets.Client;

// ============================================================
//  AevatarSecretsClient
//
//  中文说明：
//  - 这是一个轻量 .NET Client，用来调用 Secrets API（本地 sidecar 或 Aevatar.Secrets.Api）
//  - 目标：让其他项目集成「Provider/Instance + ApiKey/Model/Endpoint」能力不再复制 HTTP 细节
//  - 约束：不负责鉴权（服务端本地 loopback 保护），不记录任何 secret 值
// ============================================================

public sealed class AevatarSecretsClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json;

    public AevatarSecretsClient(HttpClient httpClient, JsonSerializerOptions? jsonOptions = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _json = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    // ------------------------------------------------------------
    // Catalog
    // ------------------------------------------------------------

    public async Task<IReadOnlyList<LlmProviderTypeItem>> ListProviderTypesAsync(CancellationToken ct = default)
    {
        var res = await _http.GetFromJsonAsync<LlmProvidersResponse>("/api/llm/providers", _json, ct);
        EnsureOk(res?.Ok == true, res?.Error);
        return res!.Providers ?? Array.Empty<LlmProviderTypeItem>();
    }

    public async Task<IReadOnlyList<LlmProviderInstanceItem>> ListInstancesAsync(CancellationToken ct = default)
    {
        var res = await _http.GetFromJsonAsync<LlmInstancesResponse>("/api/llm/instances", _json, ct);
        EnsureOk(res?.Ok == true, res?.Error);
        return res!.Instances ?? Array.Empty<LlmProviderInstanceItem>();
    }

    // ------------------------------------------------------------
    // Instance details / actions
    // ------------------------------------------------------------

    public async Task<LlmProviderPublic> GetProviderAsync(string providerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("providerName is required", nameof(providerName));

        var res = await _http.GetFromJsonAsync<LlmProviderResponse>($"/api/llm/provider/{Uri.EscapeDataString(providerName)}", _json, ct);
        EnsureOk(res?.Ok == true, res?.Error);
        return res!.Provider!;
    }

    public async Task<LlmUpsertInstanceResult> UpsertInstanceAsync(LlmUpsertInstanceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resp = await _http.PostAsJsonAsync("/api/llm/instance", request, _json, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        var res = SafeDeserialize<LlmUpsertInstanceResult>(text);
        if (!resp.IsSuccessStatusCode || res?.Ok != true)
        {
            throw new HttpRequestException($"Upsert instance failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {TrimForUi(res?.Error ?? text)}");
        }
        return res!;
    }

    public async Task<LlmModelsResult> FetchModelsAsync(string providerName, int limit = 200, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("providerName is required", nameof(providerName));
        if (limit <= 0) limit = 200;

        var res = await _http.GetFromJsonAsync<LlmModelsResult>(
            $"/api/llm/models/{Uri.EscapeDataString(providerName)}?limit={limit}",
            _json,
            ct);

        EnsureOk(res?.Ok == true, res?.Error);
        return res!;
    }

    public async Task<LlmTestResult> TestAsync(string providerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("providerName is required", nameof(providerName));

        var res = await _http.GetFromJsonAsync<LlmTestResult>($"/api/llm/test/{Uri.EscapeDataString(providerName)}", _json, ct);
        EnsureOk(res?.Ok == true, res?.Error);
        return res!;
    }

    public async Task<LlmApiKeyStatus> GetApiKeyStatusAsync(string providerName, bool reveal = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("providerName is required", nameof(providerName));

        var url = $"/api/llm/api-key/{Uri.EscapeDataString(providerName)}" + (reveal ? "?reveal=true" : "");
        var res = await _http.GetFromJsonAsync<LlmApiKeyStatus>(url, _json, ct);
        EnsureOk(res?.Ok == true, res?.Error);
        return res!;
    }

    public async Task DeleteApiKeyAsync(string providerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("providerName is required", nameof(providerName));

        var resp = await _http.DeleteAsync($"/api/llm/api-key/{Uri.EscapeDataString(providerName)}", ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Delete api key failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {TrimForUi(text)}");
    }

    // ------------------------------------------------------------
    // Advanced generic secrets
    // ------------------------------------------------------------

    public async Task SetSecretAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key is required", nameof(key));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("value is required", nameof(value));

        var resp = await _http.PostAsJsonAsync("/api/secrets/set", new { key, value }, _json, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Set secret failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {TrimForUi(text)}");
    }

    public async Task RemoveSecretAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key is required", nameof(key));

        var resp = await _http.PostAsJsonAsync("/api/secrets/remove", new { key }, _json, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Remove secret failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} {TrimForUi(text)}");
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static void EnsureOk(bool ok, string? error)
    {
        if (ok) return;
        throw new HttpRequestException($"Secrets API call failed: {TrimForUi(error ?? "unknown error")}");
    }

    private static string TrimForUi(string text, int max = 500)
    {
        var s = (text ?? string.Empty).Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    private T? SafeDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, _json);
        }
        catch
        {
            return default;
        }
    }
}

// ============================================================
//  Models (DTOs)
// ============================================================

public sealed record LlmProviderTypeItem
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public bool Recommended { get; init; }
    public int ConfiguredInstancesCount { get; init; }
}

public sealed record LlmProviderInstanceItem
{
    public string? Name { get; init; }
    public string? ProviderType { get; init; }
    public string? ProviderDisplayName { get; init; }
    public string? Model { get; init; }
    public string? Endpoint { get; init; }
}

public sealed record LlmProviderPublic
{
    public string? ProviderName { get; init; }
    public string? ProviderType { get; init; }
    public string? ProviderTypeSource { get; init; }
    public string? DisplayName { get; init; }
    public string? Kind { get; init; }
    public bool ApiKeyConfigured { get; init; }
    public string? Endpoint { get; init; }
    public string? EndpointSource { get; init; }
    public string? Model { get; init; }
    public string? ModelSource { get; init; }
}

public sealed record LlmUpsertInstanceRequest
{
    [JsonPropertyName("providerName")]
    public string? ProviderName { get; init; }

    [JsonPropertyName("providerType")]
    public string? ProviderType { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    // Optional: direct key (only when user is entering a new key)
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    // Optional: server-side copy from an existing instance (never echoes key)
    [JsonPropertyName("copyApiKeyFrom")]
    public string? CopyApiKeyFrom { get; init; }
}

public sealed record LlmUpsertInstanceResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? ProviderName { get; init; }
    public string? ProviderType { get; init; }
    public string[]? KeyPaths { get; init; }
    public LlmProviderPublic? Provider { get; init; }
}

public sealed record LlmModelsResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? ProviderName { get; init; }
    public string? Endpoint { get; init; }
    public string[]? Models { get; init; }
}

public sealed record LlmTestResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? ProviderName { get; init; }
    public string? Kind { get; init; }
    public string? Endpoint { get; init; }
    public int? LatencyMs { get; init; }
    public int? ModelsCount { get; init; }
}

public sealed record LlmApiKeyStatus
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? ProviderName { get; init; }
    public bool Configured { get; init; }
    public string? Masked { get; init; }
    public string? Value { get; init; }
}

internal sealed record LlmProvidersResponse
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public LlmProviderTypeItem[]? Providers { get; init; }
}

internal sealed record LlmInstancesResponse
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public LlmProviderInstanceItem[]? Instances { get; init; }
}

internal sealed record LlmProviderResponse
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public LlmProviderPublic? Provider { get; init; }
}


