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

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/", () => Results.Text(HtmlAssets.IndexHtml, "text/html"));

// ------------------------------------------------------------
// Providers catalog (UI)
// - Returns provider list with "connected" status (never returns secret values).
// ------------------------------------------------------------
app.MapGet("/api/llm/providers", (IAevatarUserSecretsStore secrets, HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providers = ProviderCatalog.Build(secrets);
    return Results.Json(new { ok = true, providers });
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

app.Run();

static bool IsLocal(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    return ip == null || System.Net.IPAddress.IsLoopback(ip);
}

sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

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
    bool Recommended = false);

sealed record ResolvedProviderPublic(
    string ProviderName,
    string DisplayName,
    string Kind,
    bool ApiKeyConfigured,
    string Endpoint,
    string EndpointSource);

sealed record ResolvedProvider(
    string ProviderName,
    string DisplayName,
    LlmProviderKind Kind,
    string Endpoint,
    string EndpointSource,
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

        var profile = ProviderProfiles.Get(name);

        var apiKeyPath = $"LLMProviders:Providers:{name}:ApiKey";
        var endpointPath = $"LLMProviders:Providers:{name}:Endpoint";

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

        var pub = new ResolvedProviderPublic(
            ProviderName: name,
            DisplayName: profile.DisplayName,
            Kind: profile.Kind.ToString(),
            ApiKeyConfigured: apiKeyConfigured,
            Endpoint: endpoint,
            EndpointSource: endpointSource);

        return new ResolvedProvider(
            ProviderName: name,
            DisplayName: profile.DisplayName,
            Kind: profile.Kind,
            Endpoint: endpoint,
            EndpointSource: endpointSource,
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
        new ProviderProfile("openai", "OpenAI", "popular", "Connect with API key", LlmProviderKind.OpenAiCompatible, "https://api.openai.com", Recommended: true),
        new ProviderProfile("anthropic", "Anthropic", "popular", "Connect with Claude API key", LlmProviderKind.Anthropic, "https://api.anthropic.com"),
        new ProviderProfile("google", "Google", "popular", "Connect with Gemini API key", LlmProviderKind.Google, "https://generativelanguage.googleapis.com"),
        new ProviderProfile("openrouter", "OpenRouter", "popular", "Bring your own key (OpenAI compatible)", LlmProviderKind.OpenAiCompatible, "https://openrouter.ai/api/v1"),

        // Other (common in Aevatar demos)
        new ProviderProfile("deepseek", "DeepSeek", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.deepseek.com"),
        new ProviderProfile("dashscope", "DashScope", "other", "Alibaba Qwen API key", LlmProviderKind.OpenAiCompatible, "https://dashscope.aliyuncs.com/compatible-mode"),
        new ProviderProfile("groq", "Groq", "other", "OpenAI-compatible API key", LlmProviderKind.OpenAiCompatible, "https://api.groq.com/openai"),
        new ProviderProfile("mistral", "Mistral", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.mistral.ai"),
        new ProviderProfile("together", "Together", "other", "API key", LlmProviderKind.OpenAiCompatible, "https://api.together.xyz"),

        // Azure OpenAI is supported by Aevatar runtime but probing it is not stable without api-version/deployment info.
        new ProviderProfile("azureopenai", "Azure OpenAI", "other", "Azure key (requires endpoint in appsettings)", LlmProviderKind.OpenAiCompatible, "", Recommended: false),
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
        return new ProviderProfile(name, name, "configured", "Configured via user secrets", LlmProviderKind.OpenAiCompatible, "");
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
    public static IReadOnlyList<ProviderItem> Build(IAevatarUserSecretsStore secrets)
    {
        // NOTE: "Id" here is also the default providerName we write into:
        //   LLMProviders:Providers:{providerName}:ApiKey
        // Apps can still use any custom provider name; those will show up under "Configured" once written.
        var presets = new[]
        {
            // Popular
            new ProviderPreset("openai", "OpenAI", "popular", "Connect with API key"),
            new ProviderPreset("anthropic", "Anthropic", "popular", "Connect with Claude API key"),
            new ProviderPreset("google", "Google", "popular", "Connect with Gemini API key"),
            new ProviderPreset("openrouter", "OpenRouter", "popular", "Bring your own key (OpenAI compatible)"),

            // Other (common in Aevatar demos)
            new ProviderPreset("deepseek", "DeepSeek", "other", "OpenAI-compatible API key"),
            new ProviderPreset("dashscope", "DashScope", "other", "Alibaba Qwen API key"),
            new ProviderPreset("azureopenai", "Azure OpenAI", "other", "Azure key (requires endpoint in appsettings)"),
            new ProviderPreset("groq", "Groq", "other", "OpenAI-compatible API key"),
            new ProviderPreset("mistral", "Mistral", "other", "API key"),
            new ProviderPreset("together", "Together", "other", "API key"),
        };

        var dict = new Dictionary<string, ProviderItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in presets)
        {
            var keyPath = $"LLMProviders:Providers:{p.Id}:ApiKey";
            var connected = secrets.TryGet(keyPath, out var v) && !string.IsNullOrWhiteSpace(v);
            dict[p.Id] = new ProviderItem(
                Id: p.Id,
                DisplayName: p.DisplayName,
                Category: connected ? "configured" : p.Category,
                Description: p.Description,
                Recommended: p.Recommended,
                Connected: connected);
        }

        foreach (var name in ExtractConfiguredProviderNames(secrets))
        {
            if (dict.ContainsKey(name))
                continue;

            dict[name] = new ProviderItem(
                Id: name,
                DisplayName: name,
                Category: "configured",
                Description: "Configured via user secrets",
                Recommended: false,
                Connected: true);
        }

        static int Rank(string c) =>
            string.Equals(c, "configured", StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(c, "popular", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

        return dict.Values
            .OrderBy(x => Rank(x.Category))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> ExtractConfiguredProviderNames(IAevatarUserSecretsStore secrets)
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

static class HtmlAssets
{
    public const string IndexHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Aevatar Secrets</title>
  <style>
    :root {
      --bg: #f6f7fb;
      --panel: #ffffff;
      --border: #e5e7eb;
      --muted: #6b7280;
      --text: #0f172a;
      --brand: #4f46e5;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial;
      background: var(--bg);
      color: var(--text);
    }
    .overlay {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 28px;
    }
    .modal {
      width: 720px;
      max-width: calc(100vw - 56px);
      background: var(--panel);
      border: 1px solid var(--border);
      border-radius: 16px;
      box-shadow: 0 24px 80px rgba(15, 23, 42, 0.15);
      overflow: hidden;
    }
    .hdr {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 18px;
      border-bottom: 1px solid var(--border);
    }
    .hdr .title,
    .topbar .title {
      font-size: 20px;
      font-weight: 700;
      letter-spacing: -0.01em;
    }
    .icon-btn {
      width: 34px;
      height: 34px;
      border: none;
      background: transparent;
      border-radius: 10px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      color: var(--muted);
      font-size: 18px;
      line-height: 1;
    }
    .icon-btn:hover { background: #f3f4f6; color: var(--text); }
    .content { padding: 16px 18px 18px; }
    .search {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 12px;
      border: 1px solid var(--border);
      background: #f9fafb;
      border-radius: 12px;
    }
    .search .ic { color: var(--muted); }
    .search input { border: none; outline: none; background: transparent; width: 100%; font-size: 14px; }
    .section { margin-top: 16px; }
    .section-title { font-size: 12px; font-weight: 700; color: var(--muted); margin: 12px 0 8px; }
    .list { border: 1px solid var(--border); border-radius: 12px; overflow: hidden; }
    .item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 12px;
      cursor: pointer;
      background: #fff;
    }
    .item:hover { background: #f9fafb; }
    .item + .item { border-top: 1px solid var(--border); }
    .logo {
      width: 30px;
      height: 30px;
      border-radius: 10px;
      border: 1px solid var(--border);
      background: #fff;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 800;
      color: var(--text);
      user-select: none;
      flex-shrink: 0;
    }
    .item-main { flex: 1; min-width: 0; }
    .item-name { display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 650; }
    .item-desc { font-size: 12px; color: var(--muted); margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .badge { font-size: 11px; padding: 2px 8px; border-radius: 999px; border: 1px solid var(--border); background: #f9fafb; color: var(--muted); font-weight: 650; }
    .badge.rec { color: var(--brand); border-color: rgba(79,70,229,0.25); background: rgba(79,70,229,0.08); }
    .badge.ok { color: #059669; border-color: rgba(16,185,129,0.25); background: rgba(16,185,129,0.08); }
    .chev { color: var(--muted); font-size: 18px; }
    .hint { margin-top: 14px; font-size: 12px; color: var(--muted); line-height: 1.5; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas; }

    /* Forms */
    .topbar {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px 18px;
      border-bottom: 1px solid var(--border);
    }
    .spacer { flex: 1; }
    .form { padding: 18px; }
    .subtitle { margin: 6px 0 14px; font-size: 13px; color: var(--muted); line-height: 1.5; }
    label { display: block; font-size: 12px; font-weight: 700; color: var(--muted); margin: 12px 0 6px; }
    input[type="text"], input[type="password"] {
      width: 100%;
      padding: 11px 12px;
      border-radius: 12px;
      border: 1px solid var(--border);
      background: #fff;
      font-size: 14px;
    }
    input:focus { outline: none; border-color: rgba(79,70,229,0.55); box-shadow: 0 0 0 3px rgba(79,70,229,0.12); }
    .row { display: flex; gap: 10px; align-items: center; }
    .grow { flex: 1; }
    .btn {
      padding: 11px 12px;
      border-radius: 12px;
      border: 1px solid var(--border);
      background: #fff;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      color: var(--text);
    }
    .btn:hover { background: #f9fafb; }
    .btn.primary { background: var(--brand); border-color: rgba(79,70,229,0.85); color: #fff; }
    .btn.primary:hover { background: #4338ca; }
    .btn.danger { border-color: rgba(239,68,68,0.35); color: #e11d48; }
    .btn:disabled { opacity: 0.6; cursor: not-allowed; }
    .actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 14px; }
    .msg { margin-top: 10px; font-size: 12px; color: var(--muted); white-space: pre-wrap; }
    .msg.ok { color: #059669; }
    .msg.err { color: #e11d48; }
    .box {
      margin-top: 10px;
      border: 1px solid var(--border);
      border-radius: 12px;
      background: #f9fafb;
      padding: 10px 12px;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas;
      font-size: 12px;
      color: var(--text);
      max-height: 260px;
      overflow: auto;
      white-space: pre;
    }
    .hidden { display: none; }
  </style>
</head>
<body>
  <div class="overlay">
    <div class="modal">
      <!-- LIST VIEW -->
      <div id="viewList">
        <div class="hdr">
          <div class="title">Connect provider</div>
          <button class="icon-btn" id="closeBtn" title="Close">✕</button>
        </div>
        <div class="content">
          <div class="search">
            <span class="ic">⌕</span>
            <input id="searchInput" placeholder="Search providers" />
          </div>

          <div class="section hidden" id="secConfigured">
            <div class="section-title">Configured</div>
            <div class="list" id="listConfigured"></div>
          </div>

          <div class="section">
            <div class="section-title">Popular</div>
            <div class="list" id="listPopular"></div>
          </div>

          <div class="section">
            <div class="section-title">Other</div>
            <div class="list" id="listOther"></div>
          </div>

          <div class="section">
            <div class="section-title">Advanced</div>
            <div class="list">
              <div class="item" id="advancedItem">
                <div class="logo">⚙</div>
                <div class="item-main">
                  <div class="item-name">Custom key/value <span class="badge">Advanced</span></div>
                  <div class="item-desc">Set any IConfiguration key (values are never echoed)</div>
                </div>
                <div class="chev">›</div>
              </div>
            </div>
          </div>

          <div class="hint">
            Saves into <span class="mono">~/.aevatar/secrets.json</span> (encrypted). Overrides:
            <span class="mono">AEVATAR_SECRETS_PATH</span> / <span class="mono">AEVATAR_SECRETS_DIR</span>.
            <br />
            All write APIs are <b>localhost-only</b>.
          </div>
        </div>
      </div>

      <!-- CONNECT VIEW -->
      <div id="viewConnect" class="hidden">
        <div class="topbar">
          <button class="icon-btn" id="backBtn" title="Back">←</button>
          <div class="title" id="connectTitle">Connect</div>
          <div class="spacer"></div>
          <button class="icon-btn" id="connectCloseBtn" title="Close">✕</button>
        </div>
        <div class="form">
          <div class="subtitle" id="connectSubtitle"></div>

          <label>Provider name</label>
          <input id="providerNameInput" type="text" placeholder="e.g. openai" />

          <label>Endpoint</label>
          <input id="endpointInput" type="text" placeholder="e.g. https://api.openai.com" />
          <div class="hint" id="endpointMeta" style="margin-top: 6px;"></div>

          <label>API key</label>
          <div class="row">
            <div class="grow">
              <input id="apiKeyInput" type="password" placeholder="API key" />
            </div>
            <button class="btn" id="toggleKeyBtn" type="button">Show</button>
          </div>

          <div class="actions">
            <button class="btn primary" id="submitBtn">Save</button>
            <button class="btn" id="testBtn" type="button">Test</button>
            <button class="btn" id="modelsBtn" type="button">Fetch models</button>
            <button class="btn danger" id="disconnectBtn">Disconnect</button>
          </div>

          <div id="connectMsg" class="msg"></div>
          <div id="modelsBox" class="box hidden"></div>

          <div class="hint" style="margin-top: 16px;">
            Writes <span class="mono">LLMProviders:Providers:&lt;providerName&gt;:ApiKey</span> into user secrets.
            <br />
            Tip: after changing keys, start a new session/run in your app to re-initialize providers.
          </div>
        </div>
      </div>

      <!-- ADVANCED VIEW -->
      <div id="viewAdvanced" class="hidden">
        <div class="topbar">
          <button class="icon-btn" id="advBackBtn" title="Back">←</button>
          <div class="title">Custom key/value</div>
          <div class="spacer"></div>
          <button class="icon-btn" id="advCloseBtn" title="Close">✕</button>
        </div>
        <div class="form">
          <div class="subtitle">Set any IConfiguration key/value. Values are never echoed back.</div>

          <label>Key</label>
          <input id="advKeyInput" type="text" placeholder="e.g. ConnectionStrings:MongoDB" />

          <label>Value</label>
          <div class="row">
            <div class="grow">
              <input id="advValueInput" type="password" placeholder="value" />
            </div>
            <button class="btn" id="advToggleBtn" type="button">Show</button>
          </div>

          <div class="actions">
            <button class="btn primary" id="advSaveBtn">Save</button>
            <button class="btn danger" id="advRemoveBtn">Remove</button>
          </div>

          <div id="advMsg" class="msg"></div>

          <div class="hint" style="margin-top: 16px;">
            Recommended key example: <span class="mono">LLMProviders:Providers:deepseek:ApiKey</span>
          </div>
        </div>
      </div>
    </div>
  </div>

  <script>
    const $ = (id) => document.getElementById(id);
    const state = {
      providers: [],
      selectedId: "",
      search: "",
      keyShown: false,
      // If user starts typing a new key, we should not overwrite the input while refreshing details.
      isNewKeyDraft: false,
      hasExistingKey: false,
      existingKeyMasked: "",
      existingKeyFull: "",
      advShown: false,
      endpointOriginal: "",
      endpointSource: "",
    };
    const categoryOrder = { configured: 0, popular: 1, other: 2 };
    const safeText = (s) => String(s || "");
    const upper1 = (s) => safeText(s).trim().slice(0, 1).toUpperCase();
    const isEmpty = (s) => !safeText(s).trim();
    const debounce = (fn, ms) => {
      let t = null;
      return (...args) => {
        if (t) window.clearTimeout(t);
        t = window.setTimeout(() => fn(...args), ms);
      };
    };

    function setView(view) {
      $("viewList").classList.toggle("hidden", view !== "list");
      $("viewConnect").classList.toggle("hidden", view !== "connect");
      $("viewAdvanced").classList.toggle("hidden", view !== "advanced");
    }

    function findProvider(id) {
      const key = String(id || "").toLowerCase();
      return state.providers.find((p) => String(p.id || "").toLowerCase() === key) || null;
    }

    function matches(p, q) {
      const hay = (safeText(p.displayName) + " " + safeText(p.id) + " " + safeText(p.description)).toLowerCase();
      return hay.includes(q);
    }

    async function refreshProviders() {
      try {
        const res = await fetch("/api/llm/providers");
        if (!res.ok) throw new Error("HTTP " + res.status);
        const json = await res.json();
        state.providers = Array.isArray(json.providers) ? json.providers : [];
      } catch (e) {
        console.error(e);
        state.providers = [];
      }
      renderList();
    }

    function renderSection(containerId, items) {
      const root = $(containerId);
      root.innerHTML = "";
      for (const p of items) {
        const row = document.createElement("div");
        row.className = "item";
        row.onclick = () => openConnect(p.id);

        const logo = document.createElement("div");
        logo.className = "logo";
        logo.textContent = upper1(p.displayName || p.id);

        const main = document.createElement("div");
        main.className = "item-main";

        const name = document.createElement("div");
        name.className = "item-name";
        name.appendChild(document.createTextNode(safeText(p.displayName || p.id)));

        if (p.recommended) {
          const b = document.createElement("span");
          b.className = "badge rec";
          b.textContent = "Recommended";
          name.appendChild(b);
        }
        if (p.connected) {
          const b = document.createElement("span");
          b.className = "badge ok";
          b.textContent = "Connected";
          name.appendChild(b);
        }

        const desc = document.createElement("div");
        desc.className = "item-desc";
        desc.textContent = safeText(p.description || "");

        main.appendChild(name);
        main.appendChild(desc);

        const chev = document.createElement("div");
        chev.className = "chev";
        chev.textContent = "›";

        row.appendChild(logo);
        row.appendChild(main);
        row.appendChild(chev);
        root.appendChild(row);
      }
    }

    function renderList() {
      const q = safeText(state.search).trim().toLowerCase();
      const all = q ? state.providers.filter((p) => matches(p, q)) : state.providers.slice();
      all.sort((a, b) => {
        const ra = categoryOrder[a.category] ?? 9;
        const rb = categoryOrder[b.category] ?? 9;
        if (ra !== rb) return ra - rb;
        return safeText(a.displayName).localeCompare(safeText(b.displayName), undefined, { sensitivity: "base" });
      });

      const configured = all.filter((p) => safeText(p.category) === "configured");
      const popular = all.filter((p) => safeText(p.category) === "popular");
      const other = all.filter((p) => safeText(p.category) === "other");

      $("secConfigured").classList.toggle("hidden", configured.length === 0);
      if (configured.length > 0) renderSection("listConfigured", configured);
      renderSection("listPopular", popular);
      renderSection("listOther", other);
    }

    function openConnect(id) {
      state.selectedId = id;
      state.keyShown = false;
      state.isNewKeyDraft = false;
      state.hasExistingKey = false;
      state.existingKeyMasked = "";
      state.existingKeyFull = "";

      const p = findProvider(id) || { id, displayName: id, description: "", connected: false };

      $("connectTitle").textContent = "Connect " + safeText(p.displayName || p.id);
      $("connectSubtitle").textContent =
        "Enter your " + safeText(p.displayName || p.id) + " API key to connect your account and use it in Aevatar apps.";

      $("providerNameInput").value = safeText(p.id || id);
      $("endpointInput").value = "";
      $("endpointMeta").textContent = "";
      $("apiKeyInput").value = "";
      $("apiKeyInput").type = "password";
      $("toggleKeyBtn").textContent = "Show";
      $("disconnectBtn").disabled = true;
      $("testBtn").disabled = true;
      $("modelsBtn").disabled = true;
      setModelsBox("");

      setConnectMsg("");
      updateSubmitEnabled();
      setView("connect");

      void loadProviderDetails(safeText(p.id || id));
    }

    function openAdvanced() {
      state.advShown = false;
      $("advKeyInput").value = "";
      $("advValueInput").value = "";
      $("advValueInput").type = "password";
      $("advToggleBtn").textContent = "Show";
      setAdvMsg("");
      setView("advanced");
      updateAdvancedButtons();
    }

    function setConnectMsg(text, kind) {
      const el = $("connectMsg");
      el.textContent = safeText(text);
      el.className = "msg";
      if (kind === "ok") el.classList.add("ok");
      if (kind === "err") el.classList.add("err");
    }

    function setAdvMsg(text, kind) {
      const el = $("advMsg");
      el.textContent = safeText(text);
      el.className = "msg";
      if (kind === "ok") el.classList.add("ok");
      if (kind === "err") el.classList.add("err");
    }

    function updateSubmitEnabled() {
      const pn = safeText($("providerNameInput").value).trim();
      const key = safeText($("apiKeyInput").value).trim();
      // Safety:
      // - Never allow saving when the input is just a masked/stored display.
      // - Only enable Save when user is actively drafting a new key.
      $("submitBtn").disabled = isEmpty(pn) || isEmpty(key) || state.isNewKeyDraft !== true;
    }

    function setModelsBox(text) {
      const box = $("modelsBox");
      const s = safeText(text);
      if (!s) {
        box.textContent = "";
        box.classList.add("hidden");
        return;
      }
      box.textContent = s;
      box.classList.remove("hidden");
    }

    async function loadProviderDetails(providerName) {
      const name = safeText(providerName).trim();
      if (isEmpty(name)) return;

      try {
        const res = await fetch("/api/llm/provider/" + encodeURIComponent(name));
        const json = await res.json().catch(() => null);
        const p = json && json.provider ? json.provider : null;
        if (!p) return;

        const ep = safeText(p.endpoint || "");
        $("endpointInput").value = ep;
        $("endpointMeta").textContent = ep
          ? `Endpoint (${safeText(p.endpointSource || "unknown")}): ${ep}`
          : `Endpoint (${safeText(p.endpointSource || "unknown")}): (empty)`;

        state.endpointOriginal = ep;
        state.endpointSource = safeText(p.endpointSource || "");

        const configured = Boolean(p.apiKeyConfigured);
        $("disconnectBtn").disabled = !configured;
        $("testBtn").disabled = !configured;
        $("modelsBtn").disabled = !configured;

        // Sync API key display (masked by default). Do NOT override when user is typing a new key.
        await loadApiKeyMask(name);
      } catch {
        // best-effort
      }
    }

    async function loadApiKeyMask(providerName) {
      if (state.isNewKeyDraft) return;

      const name = safeText(providerName).trim();
      if (isEmpty(name)) return;

      try {
        const res = await fetch("/api/llm/api-key/" + encodeURIComponent(name));
        const json = await res.json().catch(() => null);
        if (!json || json.ok !== true) return;

        state.hasExistingKey = Boolean(json.configured);
        state.existingKeyMasked = safeText(json.masked || "");
        state.existingKeyFull = "";
        state.keyShown = false;

        // Default: show masked for configured key; keep input ready for draft otherwise.
        if (state.hasExistingKey && state.existingKeyMasked) {
          $("apiKeyInput").type = "text";
          $("apiKeyInput").value = state.existingKeyMasked;
          $("toggleKeyBtn").textContent = "Show";
        } else {
          // No stored key: keep as password input for new entry.
          if (!state.isNewKeyDraft) {
            $("apiKeyInput").type = "password";
            $("apiKeyInput").value = "";
            $("toggleKeyBtn").textContent = state.keyShown ? "Hide" : "Show";
          }
        }

        updateSubmitEnabled();
      } catch {
        // best-effort
      }
    }

    async function saveEndpointOverride(providerName) {
      const name = safeText(providerName).trim();
      if (isEmpty(name)) return;

      const endpoint = safeText($("endpointInput").value).trim();
      const key = `LLMProviders:Providers:${name}:Endpoint`;

      // If unchanged and was not a secret override, avoid writing noisy defaults into secrets.
      const unchanged = endpoint === state.endpointOriginal;
      if (unchanged && state.endpointSource !== "secret") return;

      if (isEmpty(endpoint)) {
        await fetch("/api/secrets/remove", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ key }),
        });
        return;
      }

      await fetch("/api/secrets/set", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key, value: endpoint }),
      });
    }

    function updateAdvancedButtons() {
      const k = safeText($("advKeyInput").value).trim();
      const v = safeText($("advValueInput").value).trim();
      $("advSaveBtn").disabled = isEmpty(k) || isEmpty(v);
      $("advRemoveBtn").disabled = isEmpty(k);
    }

    async function submitApiKey() {
      const providerName = safeText($("providerNameInput").value).trim();
      const apiKey = safeText($("apiKeyInput").value).trim();
      if (isEmpty(providerName) || isEmpty(apiKey) || state.isNewKeyDraft !== true) return;

      $("submitBtn").disabled = true;
      setConnectMsg("");

      try {
        await saveEndpointOverride(providerName);

        const res = await fetch("/api/llm/api-key", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ providerName, apiKey }),
        });
        const text = await res.text();
        if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));
        setConnectMsg("Saved. Provider is now connected. Click Test to verify.", "ok");
        $("apiKeyInput").value = "";
        await refreshProviders();
        await loadProviderDetails(providerName);
      } catch (e) {
        setConnectMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        updateSubmitEnabled();
      }
    }

    async function testConnection() {
      const providerName = safeText($("providerNameInput").value).trim();
      if (isEmpty(providerName)) return;

      $("testBtn").disabled = true;
      setConnectMsg("");
      setModelsBox("");

      try {
        const res = await fetch("/api/llm/test/" + encodeURIComponent(providerName));
        const json = await res.json().catch(() => null);
        if (!json) throw new Error("bad response");

        if (json.ok === true) {
          const ms = typeof json.latencyMs === "number" ? json.latencyMs : null;
          const cnt = typeof json.modelsCount === "number" ? json.modelsCount : null;
          setConnectMsg(`OK${ms != null ? ` · ${ms}ms` : ""}${cnt != null ? ` · models=${cnt}` : ""}`, "ok");

          const sample = Array.isArray(json.sampleModels) ? json.sampleModels : [];
          if (sample.length > 0) {
            setModelsBox(sample.join("\n"));
          }
        } else {
          setConnectMsg(`Test failed: ${safeText(json.error || "unknown error")}`, "err");
        }
      } catch (e) {
        setConnectMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        await loadProviderDetails(providerName);
      }
    }

    async function fetchModels() {
      const providerName = safeText($("providerNameInput").value).trim();
      if (isEmpty(providerName)) return;

      $("modelsBtn").disabled = true;
      setConnectMsg("");
      setModelsBox("");

      try {
        const res = await fetch("/api/llm/models/" + encodeURIComponent(providerName) + "?limit=200");
        const json = await res.json().catch(() => null);
        if (!json) throw new Error("bad response");

        if (json.ok === true) {
          const arr = Array.isArray(json.models) ? json.models : [];
          setConnectMsg(`Fetched models: ${arr.length}`, "ok");
          setModelsBox(arr.join("\n"));
        } else {
          setConnectMsg(`Fetch models failed: ${safeText(json.error || "unknown error")}`, "err");
        }
      } catch (e) {
        setConnectMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        await loadProviderDetails(providerName);
      }
    }

    async function disconnectApiKey() {
      const providerName = safeText($("providerNameInput").value).trim();
      if (isEmpty(providerName)) return;

      $("disconnectBtn").disabled = true;
      setConnectMsg("");

      try {
        const res = await fetch("/api/llm/api-key/" + encodeURIComponent(providerName), { method: "DELETE" });
        const text = await res.text();
        if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));
        setConnectMsg("Disconnected.", "ok");
        await refreshProviders();
      } catch (e) {
        setConnectMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        const p = findProvider(providerName);
        $("disconnectBtn").disabled = !(p && p.connected);
      }
    }

    async function saveRaw() {
      const key = safeText($("advKeyInput").value).trim();
      const value = safeText($("advValueInput").value).trim();
      if (isEmpty(key) || isEmpty(value)) return;

      $("advSaveBtn").disabled = true;
      setAdvMsg("");

      try {
        const res = await fetch("/api/secrets/set", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ key, value }),
        });
        const text = await res.text();
        if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));
        setAdvMsg("Saved.", "ok");
        $("advValueInput").value = "";
        await refreshProviders();
      } catch (e) {
        setAdvMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        updateAdvancedButtons();
      }
    }

    async function removeRaw() {
      const key = safeText($("advKeyInput").value).trim();
      if (isEmpty(key)) return;

      $("advRemoveBtn").disabled = true;
      setAdvMsg("");

      try {
        const res = await fetch("/api/secrets/remove", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ key }),
        });
        const text = await res.text();
        if (!res.ok) throw new Error("HTTP " + res.status + (text ? (": " + text) : ""));
        setAdvMsg("Removed (if existed).", "ok");
        await refreshProviders();
      } catch (e) {
        setAdvMsg(e && e.message ? e.message : String(e), "err");
      } finally {
        updateAdvancedButtons();
      }
    }

    function wire() {
      $("searchInput").addEventListener("input", debounce((e) => {
        state.search = e.target.value || "";
        renderList();
      }, 80));

      $("closeBtn").onclick = () => {
        state.search = "";
        $("searchInput").value = "";
        renderList();
      };

      $("advancedItem").onclick = () => openAdvanced();

      $("backBtn").onclick = () => { setView("list"); setConnectMsg(""); refreshProviders(); };
      $("connectCloseBtn").onclick = () => { setView("list"); setConnectMsg(""); refreshProviders(); };
      $("advBackBtn").onclick = () => { setView("list"); setAdvMsg(""); refreshProviders(); };
      $("advCloseBtn").onclick = () => { setView("list"); setAdvMsg(""); refreshProviders(); };

      $("toggleKeyBtn").onclick = () => {
        const providerName = safeText($("providerNameInput").value).trim();

        // Draft mode: classic password toggle.
        if (state.isNewKeyDraft || !state.hasExistingKey) {
          state.keyShown = !state.keyShown;
          $("apiKeyInput").type = state.keyShown ? "text" : "password";
          $("toggleKeyBtn").textContent = state.keyShown ? "Hide" : "Show";
          return;
        }

        // Existing-key mode: hide shows masked string; show reveals full key (local-only).
        if (!state.keyShown) {
          // Show -> fetch full key (best-effort)
          (async () => {
            try {
              const res = await fetch(
                "/api/llm/api-key/" + encodeURIComponent(providerName) + "?reveal=true"
              );
              const json = await res.json().catch(() => null);
              if (!json || json.ok !== true || !json.value) {
                setConnectMsg("Failed to reveal key (not configured).", "err");
                return;
              }

              state.existingKeyFull = safeText(json.value || "");
              state.existingKeyMasked = safeText(json.masked || state.existingKeyMasked || "");
              state.keyShown = true;

              $("apiKeyInput").type = "text";
              $("apiKeyInput").value = state.existingKeyFull;
              $("toggleKeyBtn").textContent = "Hide";
              updateSubmitEnabled();
            } catch (e) {
              setConnectMsg(e && e.message ? e.message : String(e), "err");
            }
          })();
        } else {
          // Hide -> show masked; drop full value from memory best-effort.
          state.keyShown = false;
          state.existingKeyFull = "";

          $("apiKeyInput").type = "text";
          $("apiKeyInput").value = state.existingKeyMasked || "";
          $("toggleKeyBtn").textContent = "Show";
          updateSubmitEnabled();
        }
      };

      $("providerNameInput").addEventListener("input", debounce(updateSubmitEnabled, 60));
      $("apiKeyInput").addEventListener("focus", () => {
        // Convenience: when displaying stored key, select all so paste replaces it cleanly.
        if (!state.isNewKeyDraft && state.hasExistingKey) {
          try { $("apiKeyInput").select(); } catch {}
        }
      });
      $("apiKeyInput").addEventListener("input", debounce(() => {
        const cur = safeText($("apiKeyInput").value).trim();

        if (!state.isNewKeyDraft) {
          const equalsMasked = state.hasExistingKey && cur === safeText(state.existingKeyMasked).trim();
          const equalsFull = state.hasExistingKey && state.existingKeyFull && cur === safeText(state.existingKeyFull).trim();
          if (!equalsMasked && !equalsFull && !isEmpty(cur)) {
            // User started typing a new key: switch to draft mode (password by default).
            state.isNewKeyDraft = true;
            state.keyShown = false;
            $("apiKeyInput").type = "password";
            $("toggleKeyBtn").textContent = "Show";
            setModelsBox("");
          }
        }

        updateSubmitEnabled();
      }, 60));
      $("submitBtn").onclick = () => submitApiKey();
      $("testBtn").onclick = () => testConnection();
      $("modelsBtn").onclick = () => fetchModels();
      $("disconnectBtn").onclick = () => disconnectApiKey();

      $("advToggleBtn").onclick = () => {
        state.advShown = !state.advShown;
        $("advValueInput").type = state.advShown ? "text" : "password";
        $("advToggleBtn").textContent = state.advShown ? "Hide" : "Show";
      };
      $("advKeyInput").addEventListener("input", debounce(updateAdvancedButtons, 60));
      $("advValueInput").addEventListener("input", debounce(updateAdvancedButtons, 60));
      $("advSaveBtn").onclick = () => saveRaw();
      $("advRemoveBtn").onclick = () => removeRaw();
    }

    (async function main() {
      wire();
      setView("list");
      await refreshProviders();
    })();
  </script>
</body>
</html>
""";
}


