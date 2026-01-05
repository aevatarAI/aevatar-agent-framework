using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX REST Client Base
//
//  设计要点：
//  - 统一 HTTP + 签名 + 错误处理
//  - 不在这里做 Spot/Contract 分支（分支应该在 DI/配置层被消灭）
// ============================================================================

internal abstract class WeexApiClientBase
{
    protected readonly HttpClient Http;
    protected readonly WeexApiConfig Config;
    protected readonly ILogger Logger;
    protected readonly JsonSerializerOptions JsonOptions;

    protected WeexApiClientBase(
        HttpClient httpClient,
        IOptions<WeexApiConfig> config,
        ILogger logger)
    {
        Http = httpClient;
        Config = config.Value;
        Logger = logger;
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    // --------------------------------------------------------------------
    //  BaseUrl routing (market vs trading)
    // --------------------------------------------------------------------

    protected string MarketBaseUrl =>
        !string.IsNullOrWhiteSpace(Config.MarketDataBaseUrl)
            ? Config.MarketDataBaseUrl.Trim().TrimEnd('/')
            : (Config.BaseUrl ?? "").Trim().TrimEnd('/');

    protected string TradingBaseUrl =>
        !string.IsNullOrWhiteSpace(Config.TradingBaseUrl)
            ? Config.TradingBaseUrl.Trim().TrimEnd('/')
            : (Config.BaseUrl ?? "").Trim().TrimEnd('/');

    // --------------------------------------------------------------------
    //  HTTP helpers
    // --------------------------------------------------------------------

    protected Task<T> GetMarketAsync<T>(string requestPath, string queryString, bool requiresAuth, CancellationToken ct)
        => GetAsyncInternal<T>(MarketBaseUrl, HttpMethod.Get, requestPath, queryString, bodyJson: "", requiresAuth, ct);

    protected Task<T> GetTradingAsync<T>(string requestPath, string queryString, bool requiresAuth, CancellationToken ct)
        => GetAsyncInternal<T>(TradingBaseUrl, HttpMethod.Get, requestPath, queryString, bodyJson: "", requiresAuth, ct);

    protected Task<T> PostTradingAsync<T>(string requestPath, string queryString, object body, bool requiresAuth, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        return GetAsyncInternal<T>(TradingBaseUrl, HttpMethod.Post, requestPath, queryString, jsonBody, requiresAuth, ct);
    }

    protected Task<string> PostTradingRawAsync(string requestPath, string queryString, object body, bool requiresAuth, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        return GetRawInternalAsync(TradingBaseUrl, HttpMethod.Post, requestPath, queryString, jsonBody, requiresAuth, ct);
    }

    private async Task<T> GetAsyncInternal<T>(
        string baseUrl,
        HttpMethod method,
        string requestPath,
        string queryString,
        string bodyJson,
        bool requiresAuth,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, BuildUri(baseUrl, requestPath, queryString));
        if (method == HttpMethod.Post)
        {
            req.Content = new StringContent(bodyJson ?? "", Encoding.UTF8, "application/json");
        }

        if (requiresAuth)
        {
            AddAuthHeaders(req, method, requestPath, queryString ?? "", bodyJson ?? "");
        }

        using var resp = await Http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new WeexApiException(
                $"WEEX API HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} ({method.Method} {requestPath}{queryString}): {raw}",
                errorCode: $"HTTP_{(int)resp.StatusCode}");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions)
                   ?? throw new WeexApiException($"Failed to deserialize response from {requestPath}{queryString}: {raw}");
        }
        catch (JsonException ex)
        {
            throw new WeexApiException(
                $"Failed to deserialize response from {requestPath}{queryString}: {raw}",
                errorCode: "DESERIALIZE_ERROR",
                innerException: ex);
        }
    }

    private async Task<string> GetRawInternalAsync(
        string baseUrl,
        HttpMethod method,
        string requestPath,
        string queryString,
        string bodyJson,
        bool requiresAuth,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, BuildUri(baseUrl, requestPath, queryString));
        if (method == HttpMethod.Post)
        {
            req.Content = new StringContent(bodyJson ?? "", Encoding.UTF8, "application/json");
        }

        if (requiresAuth)
        {
            AddAuthHeaders(req, method, requestPath, queryString ?? "", bodyJson ?? "");
        }

        using var resp = await Http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new WeexApiException(
                $"WEEX API HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} ({method.Method} {requestPath}{queryString}): {raw}",
                errorCode: $"HTTP_{(int)resp.StatusCode}");
        }

        return raw;
    }

    // --------------------------------------------------------------------
    //  Auth / Signature
    // --------------------------------------------------------------------

    private void AddAuthHeaders(HttpRequestMessage request, HttpMethod method, string requestPath, string queryString, string bodyJson)
    {
        EnsureCredentials();

        // --------------------------------------------------------------
        //  AI Wars / WEEX 签名格式（参与者指南）
        //  message = timestamp + method.upper() + request_path + query_string + body
        // --------------------------------------------------------------
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(timestamp, method.Method, requestPath, queryString ?? "", bodyJson ?? "");

        request.Headers.Add("ACCESS-KEY", Config.ApiKey);
        request.Headers.Add("ACCESS-SIGN", signature);
        request.Headers.Add("ACCESS-PASSPHRASE", Config.Passphrase);
        request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("locale", "en-US");
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(Config.ApiKey) ||
            string.IsNullOrWhiteSpace(Config.ApiSecret) ||
            string.IsNullOrWhiteSpace(Config.Passphrase))
        {
            throw new WeexApiException(
                "WEEX API credentials are missing. Configure Weex:ApiKey / Weex:ApiSecret / Weex:Passphrase.",
                errorCode: "MISSING_CREDENTIALS");
        }
    }

    private string GenerateSignature(string timestamp, string method, string requestPath, string queryString, string body)
    {
        var message = timestamp + method.ToUpperInvariant() + requestPath + queryString + body;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.ApiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    // --------------------------------------------------------------------
    //  Small helpers
    // --------------------------------------------------------------------

    protected static Uri BuildUri(string baseUrl, string requestPath, string queryString)
    {
        var root = (baseUrl ?? "").Trim().TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath.Trim();
        if (!path.StartsWith("/")) path = "/" + path;

        var qs = queryString ?? "";
        if (!string.IsNullOrWhiteSpace(qs) && !qs.StartsWith("?")) qs = "?" + qs;

        return new Uri(root + path + qs, UriKind.Absolute);
    }

    protected static string GenerateClientOrderId()
        => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(100000, 999999)}";

    protected static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, out var result) ? result : 0m;

    protected static long ParseLong(string? value)
        => long.TryParse(value, out var result) ? result : 0L;

    protected static long NormalizeUnixMs(long value)
    {
        if (value <= 0) return 0;

        // If it's seconds (10-digit range), convert to ms.
        return value < 100_000_000_000L ? value * 1000 : value;
    }

    protected static TimeSpan ParseIntervalToTimeSpanOrZero(string interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
            return TimeSpan.Zero;

        var s = interval.Trim().ToLowerInvariant();
        if (s.EndsWith("m") && int.TryParse(s[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        if (s.EndsWith("h") && int.TryParse(s[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        if (s.EndsWith("d") && int.TryParse(s[..^1], out var days))
            return TimeSpan.FromDays(days);
        if (s.EndsWith("w") && int.TryParse(s[..^1], out var weeks))
            return TimeSpan.FromDays(weeks * 7);

        return TimeSpan.Zero;
    }
}


