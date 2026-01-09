using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Http.Json;

// ============================================================
//  Aevatar.Secrets.Api
//
//  A tiny local web app to configure encrypted user secrets.
//
//  Default URLs:
//   - http://localhost:6667 (requested, but Chrome blocks as ERR_UNSAFE_PORT)
//   - http://localhost:6677 (browser-friendly fallback)
//   - repo policy: never use :5000 as default/example
//
//  User secrets path:
//   - Default: ~/.aevatar/secrets.json
//   - Override: AEVATAR_SECRETS_PATH / AEVATAR_SECRETS_DIR
// ============================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Encrypted user secrets store (write).
builder.Services.AddAevatarUserSecretsStore();

// Ports
// If no URLs are configured (ASPNETCORE_URLS / --urls), listen on:
// - 6667: requested default (note: Chrome blocks some "unsafe" ports, including 6667)
// - 6677: safe fallback for browsers
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://localhost:6667", "http://localhost:6677");
}

var app = builder.Build();

// UI: serve from wwwroot/ instead of embedding huge HTML/JS in Program.cs
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Text("ok"));

// ------------------------------------------------------------
// Providers (base types) + instances (configured) for UI
// ------------------------------------------------------------
app.MapGet("/api/llm/providers", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providers = ProviderCatalog.BuildProviderTypes(secrets);
    return Results.Json(new { ok = true, providers });
});

app.MapGet("/api/llm/instances", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var instances = ProviderCatalog.BuildInstances(secrets);
    return Results.Json(new { ok = true, instances });
});

// Provider details (never returns secret values)
app.MapGet("/api/llm/provider/{providerName}", (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var resolved = LlmProviderResolver.Resolve(secrets, providerName);
    return Results.Json(new { ok = true, provider = resolved.Public });
});

// Test connection (best-effort): verifies API key + endpoint by calling list-models endpoint.
app.MapGet("/api/llm/test/{providerName}", async (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var resolved = LlmProviderResolver.Resolve(secrets, providerName);
    var result = await LlmProbe.TestAsync(resolved, ct);
    return Results.Json(result);
});

// Fetch models (best-effort): returns model ids/names (never returns secret values).
app.MapGet("/api/llm/models/{providerName}", async (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http,
    int? limit,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var resolved = LlmProviderResolver.Resolve(secrets, providerName);
    var result = await LlmProbe.FetchModelsAsync(resolved, limit ?? 200, ct);
    return Results.Json(result);
});

// API key status/mask/reveal (localhost-only)
// - Default: returns masked (middle hidden)
// - reveal=true: returns full value (for local UI only)
app.MapGet("/api/llm/api-key/{providerName}", (
    string providerName,
    bool? reveal,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (providerName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
    if (!secrets.TryGet(keyPath, out var value) || string.IsNullOrWhiteSpace(value))
    {
        return Results.Json(new
        {
            ok = true,
            providerName = name,
            configured = false,
            masked = ""
        });
    }

    var masked = SecretMask.MaskMiddle(value.Trim());

    if (reveal == true)
    {
        return Results.Json(new
        {
            ok = true,
            providerName = name,
            configured = true,
            masked,
            value = value.Trim()
        });
    }

    return Results.Json(new
    {
        ok = true,
        providerName = name,
        configured = true,
        masked
    });
});

app.MapPost("/api/llm/api-key", (
    SetLlmApiKeyRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providerName = (req.ProviderName ?? "").Trim();
    if (string.IsNullOrWhiteSpace(providerName))
        return Results.BadRequest(new { error = "providerName is required" });

    var apiKey = (req.ApiKey ?? "").Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.BadRequest(new { error = "apiKey is required" });

    var keyPath = $"LLMProviders:Providers:{providerName}:ApiKey";
    secrets.Set(keyPath, apiKey);

    return Results.Json(new { ok = true, providerName, keyPath });
});

// Upsert a provider instance (multi-model friendly).
// - Writes ProviderType/Model/Endpoint and ApiKey (direct or copied) into user secrets.
// - All values are stored under: LLMProviders:Providers:{name}:*
app.MapPost("/api/llm/instance", (
    UpsertLlmInstanceRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (req.ProviderName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { ok = false, error = "providerName is required" });

    var providerType = (req.ProviderType ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(providerType))
        return Results.BadRequest(new { ok = false, error = "providerType is required" });

    var model = (req.Model ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(model))
        return Results.BadRequest(new { ok = false, error = "model is required" });

    // ProviderType/Model are always explicit for instances.
    var providerTypePath = $"LLMProviders:Providers:{name}:ProviderType";
    var modelPath = $"LLMProviders:Providers:{name}:Model";
    secrets.Set(providerTypePath, providerType);
    secrets.Set(modelPath, model);

    // Endpoint is optional: if empty -> remove override (fall back to profile default).
    var endpointPath = $"LLMProviders:Providers:{name}:Endpoint";
    var endpoint = (req.Endpoint ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        secrets.Remove(endpointPath);
    }
    else
    {
        secrets.Set(endpointPath, endpoint);
    }

    // ApiKey: allow direct set or server-side copy (never echo).
    var apiKeyPath = $"LLMProviders:Providers:{name}:ApiKey";
    var apiKey = (req.ApiKey ?? string.Empty).Trim();
    var copyFrom = (req.CopyApiKeyFrom ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        secrets.Set(apiKeyPath, apiKey);
    }
    else if (!string.IsNullOrWhiteSpace(copyFrom))
    {
        var fromPath = $"LLMProviders:Providers:{copyFrom}:ApiKey";
        if (!secrets.TryGet(fromPath, out var fromKey) || string.IsNullOrWhiteSpace(fromKey))
            return Results.BadRequest(new { ok = false, error = "copyApiKeyFrom has no configured apiKey" });
        secrets.Set(apiKeyPath, fromKey.Trim());
    }

    var resolved = LlmProviderResolver.Resolve(secrets, name);
    return Results.Json(new
    {
        ok = true,
        providerName = name,
        providerType,
        keyPaths = new[] { providerTypePath, modelPath, endpointPath, apiKeyPath },
        provider = resolved.Public
    });
});

app.MapDelete("/api/llm/api-key/{providerName}", (
    string providerName,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var name = (providerName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "providerName is required" });

    var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
    var removed = secrets.Remove(keyPath);

    return Results.Json(new { ok = true, providerName = name, keyPath, removed });
});

app.MapPost("/api/secrets/set", (
    SetSecretRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var key = (req.Key ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key))
        return Results.BadRequest(new { error = "key is required" });

    var value = (req.Value ?? "").Trim();
    if (string.IsNullOrWhiteSpace(value))
        return Results.BadRequest(new { error = "value is required" });

    secrets.Set(key, value);
    return Results.Json(new { ok = true, key });
});

app.MapPost("/api/secrets/remove", (
    RemoveSecretRequest req,
    IAevatarUserSecretsStore secrets,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var key = (req.Key ?? "").Trim();
    if (string.IsNullOrWhiteSpace(key))
        return Results.BadRequest(new { error = "key is required" });

    var removed = secrets.Remove(key);
    return Results.Json(new { ok = true, key, removed });
});

// Fallback for non-file routes (optional, keeps UX consistent if linked with extra path).
app.MapFallbackToFile("index.html");

app.Run();

static bool IsLocal(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    return ip == null || System.Net.IPAddress.IsLoopback(ip);
}

sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

sealed record UpsertLlmInstanceRequest(
    string? ProviderName,
    string? ProviderType,
    string? Model,
    string? Endpoint,
    string? ApiKey,
    string? CopyApiKeyFrom);

sealed record SetSecretRequest(string? Key, string? Value);

sealed record RemoveSecretRequest(string? Key);

sealed record ProviderPreset(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    bool Recommended = false);

sealed record ProviderItem(
    string Id,
    string DisplayName,
    string Category,
    string Description,
    bool Recommended,
    bool Connected);

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

enum LlmProviderKind
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    Google = 2
}

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

static class LlmProviderResolver
{
    public static ResolvedProvider Resolve(IAevatarUserSecretsStore secrets, string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "default";

        // ProviderType resolution:
        // - Prefer explicit ProviderType in secrets (supports multi-instance names like openai-gpt-4o-mini).
        // - Fallback: infer from "<provider>-<model>" naming convention.
        // - Final fallback: treat providerName as providerType.
        var providerTypeSource = "missing";
        var providerType = string.Empty;
        var providerTypePath = $"LLMProviders:Providers:{name}:ProviderType";
        if (secrets.TryGet(providerTypePath, out var ptFromSecrets) && !string.IsNullOrWhiteSpace(ptFromSecrets))
        {
            providerTypeSource = "secret";
            providerType = ptFromSecrets.Trim();
        }
        else if (ProviderProfiles.TryInferProviderTypeFromInstanceName(name, out var inferred))
        {
            providerTypeSource = "inferred";
            providerType = inferred;
        }
        else
        {
            providerType = name;
        }

        var profile = ProviderProfiles.Get(providerType);

        var apiKeyPath = $"LLMProviders:Providers:{name}:ApiKey";
        var endpointPath = $"LLMProviders:Providers:{name}:Endpoint";
        var modelPath = $"LLMProviders:Providers:{name}:Model";

        var apiKeyConfigured = secrets.TryGet(apiKeyPath, out var apiKey) && !string.IsNullOrWhiteSpace(apiKey);
        apiKey = apiKeyConfigured ? apiKey : string.Empty;

        var endpointSource = "missing";
        var endpoint = string.Empty;
        if (secrets.TryGet(endpointPath, out var endpointFromSecrets) && !string.IsNullOrWhiteSpace(endpointFromSecrets))
        {
            endpointSource = "secret";
            endpoint = endpointFromSecrets.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(profile.DefaultEndpoint))
        {
            endpointSource = "default";
            endpoint = profile.DefaultEndpoint.Trim();
        }

        var modelSource = "missing";
        var model = string.Empty;
        if (secrets.TryGet(modelPath, out var modelFromSecrets) && !string.IsNullOrWhiteSpace(modelFromSecrets))
        {
            modelSource = "secret";
            model = modelFromSecrets.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(profile.DefaultModel))
        {
            modelSource = "default";
            model = profile.DefaultModel.Trim();
        }

        var pub = new ResolvedProviderPublic(
            ProviderName: name,
            ProviderType: providerType,
            ProviderTypeSource: providerTypeSource,
            DisplayName: profile.DisplayName,
            Kind: profile.Kind.ToString(),
            ApiKeyConfigured: apiKeyConfigured,
            Endpoint: endpoint,
            EndpointSource: endpointSource,
            Model: model,
            ModelSource: modelSource);

        return new ResolvedProvider(
            ProviderName: name,
            ProviderType: providerType,
            ProviderTypeSource: providerTypeSource,
            DisplayName: profile.DisplayName,
            Kind: profile.Kind,
            Endpoint: endpoint,
            EndpointSource: endpointSource,
            Model: model,
            ModelSource: modelSource,
            ApiKeyConfigured: apiKeyConfigured,
            ApiKey: apiKey,
            Public: pub);
    }
}

static class ProviderProfiles
{
    private static readonly IReadOnlyList<ProviderProfile> Profiles = new[]
    {
        // Popular
        new ProviderProfile("openai", "OpenAI", "popular", "Connect with API key", LlmProviderKind.OpenAiCompatible, "https://api.openai.com", "gpt-4o-mini", Recommended: true),
        new ProviderProfile("anthropic", "Anthropic", "popular", "Connect with Claude API key", LlmProviderKind.Anthropic, "https://api.anthropic.com", "claude-3-5-sonnet-latest"),
        new ProviderProfile("google", "Google", "popular", "Connect with Gemini API key", LlmProviderKind.Google, "https://generativelanguage.googleapis.com", "models/gemini-1.5-flash"),
        new ProviderProfile("openrouter", "OpenRouter", "popular", "Bring your own key (OpenAI compatible)", LlmProviderKind.OpenAiCompatible, "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"),

        // Other (common in Aevatar demos)
        new ProviderProfile("deepseek", "DeepSeek", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.deepseek.com", "deepseek-chat"),
        new ProviderProfile("dashscope", "DashScope", "other", "Alibaba Qwen API key", LlmProviderKind.OpenAiCompatible, "https://dashscope.aliyuncs.com/compatible-mode", "qwen-plus"),
        new ProviderProfile("groq", "Groq", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.groq.com/openai", "llama-3.1-8b-instant"),
        new ProviderProfile("mistral", "Mistral", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.mistral.ai", "mistral-small-latest"),
        new ProviderProfile("together", "Together", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.together.xyz", "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo"),

        // Azure OpenAI is supported by Aevatar runtime but probing it is not stable without api-version/deployment info.
        new ProviderProfile("azureopenai", "Azure OpenAI", "other", "Azure key (requires endpoint in appsettings)", LlmProviderKind.OpenAiCompatible, "", "", Recommended: false),
    };

    public static ProviderProfile Get(string providerName)
    {
        var name = (providerName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = Profiles.FirstOrDefault(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        // Unknown provider name: treat as OpenAI-compatible with no default endpoint.
        return new ProviderProfile(name, name, "configured", "Configured via user secrets", LlmProviderKind.OpenAiCompatible, "", "");
    }

    public static IReadOnlyList<ProviderProfile> All => Profiles;

    public static bool TryInferProviderTypeFromInstanceName(string instanceName, out string providerType)
    {
        providerType = string.Empty;
        var name = (instanceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // If instanceName matches a known provider id, treat it as that provider type.
        if (Profiles.Any(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase)))
        {
            providerType = name;
            return true;
        }

        // Convention: "<provider>-<model>"
        var idx = name.IndexOf('-', StringComparison.Ordinal);
        if (idx <= 0)
            return false;

        var head = name.Substring(0, idx).Trim();
        if (string.IsNullOrWhiteSpace(head))
            return false;

        if (Profiles.Any(p => string.Equals(p.Id, head, StringComparison.OrdinalIgnoreCase)))
        {
            providerType = head;
            return true;
        }

        return false;
    }
}

static class LlmProbe
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public static async Task<object> TestAsync(ResolvedProvider provider, CancellationToken ct)
    {
        if (!provider.ApiKeyConfigured)
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                error = "API key not configured"
            };
        }

        if (string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                error = "Endpoint is empty (set LLMProviders:Providers:<name>:Endpoint)"
            };
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var models = await FetchModelsCoreAsync(provider, max: 20, ct);
            sw.Stop();

            if (models.Ok)
            {
                return new
                {
                    ok = true,
                    providerName = provider.ProviderName,
                    kind = provider.Kind.ToString(),
                    endpoint = provider.Endpoint,
                    latencyMs = sw.ElapsedMilliseconds,
                    modelsCount = models.Models.Count,
                    sampleModels = models.Models
                };
            }

            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                latencyMs = sw.ElapsedMilliseconds,
                error = models.Error ?? "unknown error"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                error = ex.Message
            };
        }
    }

    public static async Task<object> FetchModelsAsync(ResolvedProvider provider, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, 500);

        if (!provider.ApiKeyConfigured)
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                error = "API key not configured"
            };
        }

        if (string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                error = "Endpoint is empty (set LLMProviders:Providers:<name>:Endpoint)"
            };
        }

        var result = await FetchModelsCoreAsync(provider, max, ct);
        if (result.Ok)
        {
            return new
            {
                ok = true,
                providerName = provider.ProviderName,
                kind = provider.Kind.ToString(),
                endpoint = provider.Endpoint,
                models = result.Models
            };
        }

        return new
        {
            ok = false,
            providerName = provider.ProviderName,
            kind = provider.Kind.ToString(),
            endpoint = provider.Endpoint,
            error = result.Error ?? "unknown error"
        };
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchModelsCoreAsync(
        ResolvedProvider provider,
        int max,
        CancellationToken ct)
    {
        return provider.Kind switch
        {
            LlmProviderKind.Anthropic => await FetchAnthropicModelsAsync(provider, max, ct),
            LlmProviderKind.Google => await FetchGoogleModelsAsync(provider, max, ct),
            _ => await FetchOpenAiModelsAsync(provider, max, ct)
        };
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchOpenAiModelsAsync(
        ResolvedProvider provider,
        int max,
        CancellationToken ct)
    {
        // OpenAI-compatible: GET {endpoint}/v1/models (or {endpoint}/models if endpoint already ends with /v1)
        var url = BuildOpenAiModelsUrl(provider.Endpoint);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await ReadBodyBestEffortAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, new List<string>(), $"HTTP {(int)resp.StatusCode}: {TrimForUi(body)}");
        }

        var models = ParseModelsFromJson(body, idKey: "id");
        return (true, models.Take(max).ToList(), null);
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchAnthropicModelsAsync(
        ResolvedProvider provider,
        int max,
        CancellationToken ct)
    {
        // Anthropic: GET {endpoint}/v1/models
        var url = BuildPath(provider.Endpoint, "/v1/models");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("x-api-key", provider.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await ReadBodyBestEffortAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, new List<string>(), $"HTTP {(int)resp.StatusCode}: {TrimForUi(body)}");
        }

        // Some responses use `data: [{id: ...}]`, some may use `models: [...]`.
        var models = ParseModelsFromJson(body, idKey: "id");
        if (models.Count == 0)
        {
            models = ParseModelsFromJson(body, idKey: "name");
        }

        return (true, models.Take(max).ToList(), null);
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchGoogleModelsAsync(
        ResolvedProvider provider,
        int max,
        CancellationToken ct)
    {
        // Google Gemini (Generative Language API): GET {endpoint}/v1beta/models
        var url = BuildPath(provider.Endpoint, "/v1beta/models");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("x-goog-api-key", provider.ApiKey);

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await ReadBodyBestEffortAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, new List<string>(), $"HTTP {(int)resp.StatusCode}: {TrimForUi(body)}");
        }

        var models = ParseModelsFromGoogle(body);
        return (true, models.Take(max).ToList(), null);
    }

    private static string BuildOpenAiModelsUrl(string endpoint)
    {
        var baseUrl = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{baseUrl}/models";
        return $"{baseUrl}/v1/models";
    }

    private static string BuildPath(string endpoint, string path)
    {
        var baseUrl = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        var p = path.StartsWith('/') ? path : "/" + path;
        return baseUrl + p;
    }

    private static List<string> ParseModelsFromJson(string json, string idKey)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // OpenAI: { data: [{ id: "..." }, ...] }
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                return data.EnumerateArray()
                    .Select(x =>
                    {
                        if (x.ValueKind != JsonValueKind.Object) return null;
                        if (!x.TryGetProperty(idKey, out var id)) return null;
                        return id.GetString();
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Alternative: { models: [...] }
            if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                return models.EnumerateArray()
                    .Select(x =>
                    {
                        if (x.ValueKind == JsonValueKind.String) return x.GetString();
                        if (x.ValueKind != JsonValueKind.Object) return null;
                        if (!x.TryGetProperty(idKey, out var id)) return null;
                        return id.GetString();
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch
        {
            // ignore
        }

        return new List<string>();
    }

    private static List<string> ParseModelsFromGoogle(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Google: { models: [{ name: "models/..." }, ...] }
            if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                return models.EnumerateArray()
                    .Select(x =>
                    {
                        if (x.ValueKind != JsonValueKind.Object) return null;
                        if (!x.TryGetProperty("name", out var name)) return null;
                        return name.GetString();
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch
        {
            // ignore
        }

        return new List<string>();
    }

    private static async Task<string> ReadBodyBestEffortAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TrimForUi(string text, int max = 800)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }
}

static class ProviderCatalog
{
    public static IReadOnlyList<ProviderTypeItem> BuildProviderTypes(IAevatarUserSecretsStore secrets)
    {
        var counts = BuildInstances(secrets)
            .GroupBy(x => x.ProviderType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return ProviderProfiles.All
            .OrderBy(x => string.Equals(x.Category, "popular", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProviderTypeItem(
                Id: p.Id,
                DisplayName: p.DisplayName,
                Category: p.Category,
                Description: p.Description,
                Recommended: p.Recommended,
                ConfiguredInstancesCount: counts.TryGetValue(p.Id, out var c) ? c : 0))
            .ToList();
    }

    public static IReadOnlyList<ProviderInstanceItem> BuildInstances(IAevatarUserSecretsStore secrets)
    {
        var names = ExtractConfiguredInstanceNames(secrets);
        var list = new List<ProviderInstanceItem>(names.Count);
        foreach (var name in names)
        {
            var resolved = LlmProviderResolver.Resolve(secrets, name);
            list.Add(new ProviderInstanceItem(
                Name: name,
                ProviderType: resolved.ProviderType,
                ProviderDisplayName: resolved.DisplayName,
                Model: resolved.Model,
                Endpoint: resolved.Endpoint));
        }

        return list
            .OrderBy(x => x.ProviderDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> ExtractConfiguredInstanceNames(IAevatarUserSecretsStore secrets)
    {
        var all = secrets.GetAll();
        const string prefix = "LLMProviders:Providers:";
        const string suffix = ":ApiKey";

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in all.Keys)
        {
            if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var mid = k.Substring(prefix.Length, k.Length - prefix.Length - suffix.Length);
            if (string.IsNullOrWhiteSpace(mid))
                continue;
            set.Add(mid.Trim());
        }
        return set;
    }
}