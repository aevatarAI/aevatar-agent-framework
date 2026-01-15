using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.DecisionEngines;

/// <summary>
/// Cognitive Mesh decision engine (HTTP).
///
/// It calls Cognitive Mesh service and expects a JSON decision string in response.content.
/// </summary>
public sealed class CognitiveMeshDecisionEngine : ITradingDecisionEngine
{
    private readonly HttpClient _httpClient;
    private readonly DecisionEngineConfig _config;
    private readonly ILogger<CognitiveMeshDecisionEngine> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public CognitiveMeshDecisionEngine(
        HttpClient httpClient,
        IOptions<DecisionEngineConfig> config,
        ILogger<CognitiveMeshDecisionEngine> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string Name => "CognitiveMesh";

    public async Task<string> GetDecisionJsonAsync(string prompt, string? cycleId = null, CancellationToken ct = default)
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("CognitiveMesh decision engine is not enabled/configured.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 30));

        var baseUri = new Uri(_config.CognitiveMeshBaseUrl);
        var reasonUri = new Uri(baseUri, "/api/reason");

        var body = new
        {
            strategyKind = string.IsNullOrWhiteSpace(_config.CognitiveMeshStrategy) ? "Cognitive" : _config.CognitiveMeshStrategy,
            task = prompt,
            cognitiveWorkflow = string.IsNullOrWhiteSpace(_config.CognitiveWorkflow) ? "maker-v2" : _config.CognitiveWorkflow,
            timeoutSeconds = _config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 30,
            context = string.IsNullOrWhiteSpace(cycleId)
                ? null
                : new Dictionary<string, string> { ["cycleId"] = cycleId }
        };

        _logger.LogInformation("[DecisionEngine] Calling CognitiveMesh: {Uri} strategy={Strategy} workflow={Workflow}",
            reasonUri, body.strategyKind, body.cognitiveWorkflow);

        using var req = new HttpRequestMessage(HttpMethod.Post, reasonUri)
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        using var resp = await _httpClient.SendAsync(req, timeoutCts.Token);
        var raw = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("[DecisionEngine] CognitiveMesh HTTP failed: {Status} {Reason}. Body={Body}",
                (int)resp.StatusCode, resp.ReasonPhrase, raw);
            throw new InvalidOperationException($"CognitiveMesh HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
        }

        var parsed = JsonSerializer.Deserialize<CognitiveMeshReasoningResponse>(raw, _jsonOptions);
        if (parsed == null)
            throw new InvalidOperationException("CognitiveMesh response cannot be parsed.");

        if (!parsed.Success)
            throw new InvalidOperationException($"CognitiveMesh failed: {parsed.Error ?? "unknown error"}");

        if (string.IsNullOrWhiteSpace(parsed.Content))
            throw new InvalidOperationException("CognitiveMesh returned empty content.");

        return parsed.Content;
    }

    private bool IsEnabled()
    {
        return string.Equals(_config.Mode, "CognitiveMesh", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(_config.CognitiveMeshBaseUrl);
    }

    private sealed record CognitiveMeshReasoningResponse
    {
        public bool Success { get; init; }
        public string? Content { get; init; }
        public string? Error { get; init; }
    }
}


