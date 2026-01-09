using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ScientificResearchAssistant.Api.Infrastructure;

// ============================================================
//  LlmSecretsApi
//
//  Purpose:
//  - Bring the Aevatar.Secrets.Api UX/API surface into SRA so the frontend
//    can manage providers (API keys, endpoint overrides, custom config keys)
//    without running a separate secrets server.
//
//  Notes:
//  - All write APIs are localhost-only.
//  - Never echo secret values unless reveal=true (still localhost-only).
// ============================================================
public static class LlmSecretsApi
{
    public static void MapLlmSecretsApi(this WebApplication app)
    {
        // Providers catalog (UI)
        app.MapGet("/api/llm/providers", (
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
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

        // Test connection (best-effort)
        app.MapGet("/api/llm/test/{providerName}", async (
            string providerName,
            IAevatarUserSecretsStore secrets,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var resolved = LlmProviderResolver.Resolve(secrets, providerName);
            var result = await LlmSecretsProbe.TestAsync(resolved, ct);
            return Results.Json(result);
        });

        // Fetch models (best-effort)
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
            var result = await LlmSecretsProbe.FetchModelsAsync(resolved, limit ?? 200, ct);
            return Results.Json(result);
        });

        // API key status/mask/reveal (localhost-only)
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

            var trimmed = value.Trim();
            var masked = SecretMask.MaskMiddle(trimmed);

            if (reveal == true)
            {
                return Results.Json(new
                {
                    ok = true,
                    providerName = name,
                    configured = true,
                    masked,
                    value = trimmed
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
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var apiKey = (req.ApiKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { ok = false, error = "apiKey is required" });

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
                return Results.BadRequest(new { ok = false, error = "providerName is required" });

            var keyPath = $"LLMProviders:Providers:{name}:ApiKey";
            var removed = secrets.Remove(keyPath);

            return Results.Json(new { ok = true, providerName = name, keyPath, removed });
        });

        // Generic secrets set/remove (Advanced + endpoint override).
        app.MapPost("/api/secrets/set", (
            SetSecretRequest req,
            IAevatarUserSecretsStore secrets,
            HttpContext http) =>
        {
            if (!IsLocal(http))
                return Results.Forbid();

            var key = (req.Key ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { ok = false, error = "key is required" });

            var value = (req.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return Results.BadRequest(new { ok = false, error = "value is required" });

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
                return Results.BadRequest(new { ok = false, error = "key is required" });

            var removed = secrets.Remove(key);
            return Results.Json(new { ok = true, key, removed });
        });
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip == null || System.Net.IPAddress.IsLoopback(ip);
    }

    // ------------------------------------------------------------
    // Contracts (simple JSON records)
    // ------------------------------------------------------------
    private sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);
    private sealed record SetSecretRequest(string? Key, string? Value);
    private sealed record RemoveSecretRequest(string? Key);

    // ------------------------------------------------------------
    // Provider model
    // ------------------------------------------------------------
    private enum LlmProviderKind
    {
        OpenAiCompatible = 0,
        Anthropic = 1,
        Google = 2
    }

    private sealed record ProviderItem(
        string Id,
        string DisplayName,
        string Category,
        string Description,
        bool Recommended,
        bool Connected);

    private sealed record ProviderPreset(string Id, string DisplayName, string Category, string Description, bool Recommended = false);

    private sealed record ProviderProfile(
        string Id,
        string DisplayName,
        string Category,
        string Description,
        LlmProviderKind Kind,
        string DefaultEndpoint,
        string DefaultModel,
        bool Recommended = false);

    private static class ProviderProfiles
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
    }

    private sealed record ResolvedProviderPublic(
        string ProviderName,
        string DisplayName,
        string Kind,
        bool ApiKeyConfigured,
        string Endpoint,
        string EndpointSource,
        string Model,
        string ModelSource);

    private sealed record ResolvedProvider(
        string ProviderName,
        string DisplayName,
        LlmProviderKind Kind,
        string Endpoint,
        string EndpointSource,
        string Model,
        string ModelSource,
        bool ApiKeyConfigured,
        string ApiKey,
        ResolvedProviderPublic Public);

    private static class LlmProviderResolver
    {
        public static ResolvedProvider Resolve(IAevatarUserSecretsStore secrets, string providerName)
        {
            var name = (providerName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "default";

            var profile = ProviderProfiles.Get(name);

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
                DisplayName: profile.DisplayName,
                Kind: profile.Kind.ToString(),
                ApiKeyConfigured: apiKeyConfigured,
                Endpoint: endpoint,
                EndpointSource: endpointSource,
                Model: model,
                ModelSource: modelSource);

            return new ResolvedProvider(
                ProviderName: name,
                DisplayName: profile.DisplayName,
                Kind: profile.Kind,
                Endpoint: endpoint,
                EndpointSource: endpointSource,
                Model: model,
                ModelSource: modelSource,
                ApiKeyConfigured: apiKeyConfigured,
                ApiKey: apiKeyConfigured ? apiKey!.Trim() : string.Empty,
                Public: pub);
        }
    }

    private static class ProviderCatalog
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

    private static class LlmSecretsProbe
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


