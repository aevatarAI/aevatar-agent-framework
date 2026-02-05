using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Microsoft.Extensions.Logging;

namespace Aevatar.VibeResearching.UserProviders.Application.Services;

/// <summary>
/// Handles HTTP-level connectivity testing and model listing for LLM providers.
/// Separated from UserProviderAppService for single-responsibility adherence.
/// </summary>
public sealed class ProviderConnectivityTester
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderConnectivityTester> _logger;

    public ProviderConnectivityTester(
        IHttpClientFactory httpClientFactory,
        ILogger<ProviderConnectivityTester> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Tests connectivity to a provider by calling its models endpoint.
    /// </summary>
    public async Task<ProviderTestResultDto> TestConnectivityAsync(
        string baseUrl, string apiKey, string model, CancellationToken ct)
    {
        var client = CreateAuthorizedClient(apiKey);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await client.GetAsync($"{baseUrl}/v1/models", ct);
            sw.Stop();

            return response.IsSuccessStatusCode
                ? BuildResult(true, sw.ElapsedMilliseconds, model, "Connection successful.")
                : BuildResult(false, sw.ElapsedMilliseconds, model, $"Provider returned HTTP {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException)
        {
            return BuildResult(false, sw.ElapsedMilliseconds, model, "Connection timed out.");
        }
        catch (HttpRequestException ex)
        {
            return BuildResult(false, sw.ElapsedMilliseconds, model, $"Connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches the list of available models from a provider's API.
    /// Falls back to a placeholder if the endpoint is unreachable.
    /// </summary>
    public async Task<IReadOnlyList<ProviderModelDto>> FetchModelsAsync(
        string baseUrl, string apiKey, int limit, string providerType, CancellationToken ct)
    {
        var client = CreateAuthorizedClient(apiKey);

        try
        {
            var response = await client.GetAsync($"{baseUrl}/v1/models", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Models endpoint returned {Status} for provider type {Type}.",
                    response.StatusCode, providerType);
                return FallbackModelList(providerType);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseModelsResponse(json, limit, providerType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch models from provider.");
            return FallbackModelList(providerType);
        }
    }

    /// <summary>
    /// Resolves the default API endpoint URL for a given provider type.
    /// </summary>
    public static string GetDefaultEndpoint(string providerType)
    {
        return providerType switch
        {
            "OpenAI" => "https://api.openai.com",
            "Anthropic" => "https://api.anthropic.com",
            "Google" => "https://generativelanguage.googleapis.com",
            "DeepSeek" => "https://api.deepseek.com",
            "OpenRouter" => "https://openrouter.ai/api",
            _ => "https://api.openai.com"
        };
    }

    private HttpClient CreateAuthorizedClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static ProviderTestResultDto BuildResult(bool ok, long latencyMs, string model, string message)
    {
        return new ProviderTestResultDto { Ok = ok, LatencyMs = latencyMs, Model = model, Message = message };
    }

    private static IReadOnlyList<ProviderModelDto> ParseModelsResponse(
        string json, int limit, string providerType)
    {
        using var doc = JsonDocument.Parse(json);
        var models = new List<ProviderModelDto>();

        if (!doc.RootElement.TryGetProperty("data", out var dataArray))
            return models;

        foreach (var item in dataArray.EnumerateArray())
        {
            if (models.Count >= limit) break;

            var modelId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            var ownedBy = item.TryGetProperty("owned_by", out var ownerProp) ? ownerProp.GetString() ?? providerType : providerType;

            models.Add(new ProviderModelDto { Id = modelId, Name = modelId, OwnedBy = ownedBy });
        }

        return models;
    }

    private static IReadOnlyList<ProviderModelDto> FallbackModelList(string providerType)
    {
        return new List<ProviderModelDto>
        {
            new() { Id = "(unable to fetch)", Name = "(unable to fetch)", OwnedBy = providerType }
        };
    }
}
