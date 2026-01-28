using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

public static partial class LlmSecretsApi
{
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
}


