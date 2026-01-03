/*aevatar_tool
{
  "name": "weex_ai_market_funding_time",
  "description": "WEEX AI Wars MARKET API: GET /capi/v2/market/funding_time (docs: https://www.weex.com/api-doc/contract/Market_API/GetNextContractSettlementTime)",
  "category": "Custom",
  "version": "0.1.0",
  "tags": [
    "weex",
    "ai-wars",
    "market",
    "get"
  ],
  "requiresConfirmation": false,
  "isDangerous": false,
  "timeoutMs": 20000,
  "parameters": {
    "required": [
      "symbol"
    ],
    "items": {
      "symbol": {
        "type": "string",
        "required": true,
        "description": "Trading pair"
      }
    }
  }
}
*/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

var input = await Console.In.ReadToEndAsync();

var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    // dotnet run --file 默认禁用反射序列化，这里显式开启
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

var baseUrl = GetEnv("WEEX_BASE_URL", "https://api-contract.weex.com");
var locale = GetEnv("WEEX_LOCALE", "en-US");

var apiKey = GetOptionalEnv("WEEX_API_KEY");
var apiSecret = GetOptionalEnv("WEEX_API_SECRET");
var passphrase = GetOptionalEnv("WEEX_PASSPHRASE");

using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
var root = doc.RootElement;

var required = new[] { "symbol" };
EnsureRequired(root, required);

const string requestPath = "/capi/v2/market/funding_time";
var method = "GET";

var allParams = new[] { "symbol" };

string queryString = "";
string bodyJson = "";

var httpMethod = method == "POST" ? HttpMethod.Post : HttpMethod.Get;
Uri url;

if (httpMethod == HttpMethod.Get)
{
    queryString = BuildQueryString(root, allParams);
    url = new Uri(new Uri(baseUrl.TrimEnd('/')), requestPath + queryString);
}
else
{
    var body = BuildBody(root, allParams);
    bodyJson = JsonSerializer.Serialize(body, jsonOptions);
    url = new Uri(new Uri(baseUrl.TrimEnd('/')), requestPath);
}

var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
var signature = Sign(apiSecret ?? "", timestamp, method, requestPath, queryString, bodyJson);

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("Accept", "application/json");
http.DefaultRequestHeaders.UserAgent.ParseAdd("Aevatar.Trade/1.0");
using var req = new HttpRequestMessage(httpMethod, url);

if (httpMethod == HttpMethod.Post)
{
    req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
}

if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiSecret) && !string.IsNullOrWhiteSpace(passphrase))
{
    req.Headers.Add("ACCESS-KEY", apiKey);
    req.Headers.Add("ACCESS-SIGN", signature);
    req.Headers.Add("ACCESS-PASSPHRASE", passphrase);
    req.Headers.Add("ACCESS-TIMESTAMP", timestamp);
}

req.Headers.Add("locale", locale);

try
{
    using var resp = await http.SendAsync(req);
    var raw = await resp.Content.ReadAsStringAsync();

    var ok = resp.IsSuccessStatusCode;
    JsonElement? data = null;

    try
    {
        using var respDoc = JsonDocument.Parse(raw);
        data = respDoc.RootElement.Clone();
    }
    catch
    {
        // ignore parse errors; keep raw
    }

    var result = new
    {
        success = ok,
        httpStatus = (int)resp.StatusCode,
        request = new
        {
            method,
            baseUrl,
            requestPath,
            queryString,
            body = string.IsNullOrWhiteSpace(bodyJson) ? null : bodyJson
        },
        data,
        raw
    };

    Console.WriteLine("AEVATAR_TOOL_OUTPUT:" + JsonSerializer.Serialize(result, jsonOptions));
    Environment.ExitCode = ok ? 0 : 1;
}
catch (Exception ex)
{
    var result = new
    {
        success = false,
        error = ex.Message
    };
    Console.WriteLine("AEVATAR_TOOL_OUTPUT:" + JsonSerializer.Serialize(result, jsonOptions));
    Environment.ExitCode = 1;
}

return;

static string GetEnv(string key, string fallback)
{
    var v = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
}

static string? GetOptionalEnv(string key)
{
    var v = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

static string GetRequiredEnv(string key)
{
    var v = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(v))
        throw new InvalidOperationException($"Missing env var: {key}");
    return v.Trim();
}

static void EnsureRequired(JsonElement root, string[] required)
{
    foreach (var k in required)
    {
        if (string.IsNullOrWhiteSpace(k)) continue;
        if (!root.TryGetProperty(k, out var el) || el.ValueKind == JsonValueKind.Null || (el.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(el.GetString())))
            throw new InvalidOperationException($"Missing parameter: {k}");
    }
}

static string BuildQueryString(JsonElement root, string[] keys)
{
    var parts = new List<string>();
    foreach (var k in keys)
    {
        if (!root.TryGetProperty(k, out var el) || el.ValueKind == JsonValueKind.Null)
            continue;
        var v = ToQueryValue(el);
        if (string.IsNullOrWhiteSpace(v))
            continue;
        parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
    }
    return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
}

static Dictionary<string, object?> BuildBody(JsonElement root, string[] keys)
{
    var body = new Dictionary<string, object?>();
    foreach (var k in keys)
    {
        if (!root.TryGetProperty(k, out var el) || el.ValueKind == JsonValueKind.Null)
            continue;
        body[k] = el.Clone();
    }
    return body;
}

static string ToQueryValue(JsonElement el)
{
    if (el.ValueKind == JsonValueKind.String)
        return el.GetString() ?? "";
    return el.GetRawText().Trim('"');
}

static string Sign(string apiSecret, string timestamp, string method, string requestPath, string queryString, string bodyJson)
{
    // AI Wars signature (Participant Guide):
    // message = timestamp + method.upper() + request_path + query_string + body
    var msg = timestamp + method.ToUpperInvariant() + requestPath + (queryString ?? "") + (bodyJson ?? "");
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(msg));
    return Convert.ToBase64String(hash);
}
