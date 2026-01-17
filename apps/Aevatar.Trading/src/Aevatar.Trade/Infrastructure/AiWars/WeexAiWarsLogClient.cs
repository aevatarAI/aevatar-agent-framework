using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Trade;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.AiWars;

/// <summary>
/// WEEX AI Wars log upload client (skeleton).
///
/// IMPORTANT:
/// - Endpoint/path and request schema may differ from spot trading API.
/// - This client is intentionally conservative: disabled by default, and can be wired in later.
/// </summary>
public sealed class WeexAiWarsLogClient : IWeexAiWarsLogClient
{
    private readonly HttpClient _httpClient;
    private readonly AiWarsLogUploadConfig _config;
    private readonly ILogger<WeexAiWarsLogClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public WeexAiWarsLogClient(
        HttpClient httpClient,
        IOptions<AiWarsLogUploadConfig> config,
        ILogger<WeexAiWarsLogClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<AiWarsUploadResult> UploadAiLogAsync(AiWarsUploadRequest request, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return new AiWarsUploadResult
            {
                Success = false,
                ErrorCode = "DISABLED",
                ErrorMessage = "AiWars upload is disabled by configuration."
            };
        }

        if (string.IsNullOrWhiteSpace(_config.BaseUrl) || string.IsNullOrWhiteSpace(_config.UploadPath))
        {
            return new AiWarsUploadResult
            {
                Success = false,
                ErrorCode = "CONFIG_ERROR",
                ErrorMessage = "AiWars.BaseUrl or AiWars.UploadPath is empty."
            };
        }

        // NOTE:
        // We do JSON with base64 content for a simple skeleton. If AI Wars requires multipart,
        // switch this implementation to MultipartFormDataContent.
        var body = new
        {
            fileName = request.FileName,
            contentType = request.ContentType,
            cycleId = request.CycleId,
            contentBase64 = Convert.ToBase64String(request.Content)
        };

        var bodyJson = JsonSerializer.Serialize(body, _jsonOptions);
        var path = NormalizePath(_config.UploadPath);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            AddAuthHeaders(httpRequest, HttpMethod.Post, path, bodyJson);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[AiWars] Upload failed: HTTP {Status} {Reason}. Body={Body}",
                    (int)response.StatusCode, response.ReasonPhrase, raw);

                return new AiWarsUploadResult
                {
                    Success = false,
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = response.ReasonPhrase,
                    RawResponse = raw
                };
            }

            _logger.LogInformation("[AiWars] Upload completed: HTTP {Status}", (int)response.StatusCode);

            // Skeleton: return raw response (future: parse remote_id)
            return new AiWarsUploadResult
            {
                Success = true,
                RawResponse = raw
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiWars] Upload exception");
            return new AiWarsUploadResult
            {
                Success = false,
                ErrorCode = "CLIENT_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    private void AddAuthHeaders(HttpRequestMessage request, HttpMethod method, string path, string? body)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey) ||
            string.IsNullOrWhiteSpace(_config.ApiSecret) ||
            string.IsNullOrWhiteSpace(_config.Passphrase))
        {
            // Allow unauthenticated mode for local dev / until the AI Wars doc is confirmed.
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(timestamp, method.Method, path, body);

        request.Headers.Add("ACCESS-KEY", _config.ApiKey);
        request.Headers.Add("ACCESS-SIGN", signature);
        request.Headers.Add("ACCESS-PASSPHRASE", _config.Passphrase);
        request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("locale", "en-US");
    }

    private string GenerateSignature(string timestamp, string method, string path, string? body)
    {
        var message = timestamp + method.ToUpperInvariant() + path + (body ?? "");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.ApiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        return path.StartsWith('/') ? path : "/" + path;
    }
}


