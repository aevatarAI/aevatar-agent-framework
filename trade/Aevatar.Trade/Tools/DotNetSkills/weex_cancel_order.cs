/*aevatar_tool
{
  "name": "weex_cancel_order",
  "description": "Cancel an order on WEEX (authenticated). Reads credentials from env vars: WEEX_API_KEY, WEEX_API_SECRET, WEEX_PASSPHRASE. Optional: WEEX_BASE_URL (default https://api-spot.weex.com).",
  "category": "Custom",
  "version": "0.1.0",
  "tags": ["weex", "trade", "order", "cancel"],
  "requiresConfirmation": true,
  "isDangerous": true,
  "timeoutMs": 30000,
  "parameters": {
    "required": ["symbol"],
    "items": {
      "symbol": { "type": "string", "required": true, "description": "Trading pair, e.g. BTCUSDT_SPBL" },
      "order_id": { "type": "string", "required": false, "description": "WEEX order id" },
      "client_order_id": { "type": "string", "required": false, "description": "Client order id" }
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

var baseUrl = GetEnv("WEEX_BASE_URL", "https://api-spot.weex.com");
var apiKey = GetRequiredEnv("WEEX_API_KEY");
var apiSecret = GetRequiredEnv("WEEX_API_SECRET");
var passphrase = GetRequiredEnv("WEEX_PASSPHRASE");

using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
var root = doc.RootElement;

var symbol = GetRequiredString(root, "symbol");
var orderId = GetOptionalString(root, "order_id", null);
var clientOrderId = GetOptionalString(root, "client_order_id", null);

if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(clientOrderId))
{
    throw new InvalidOperationException("Either 'order_id' or 'client_order_id' must be provided.");
}

var path = "/api/v2/trade/cancel-order";
var url = new Uri(new Uri(baseUrl), path);

var bodyObj = new Dictionary<string, object?>
{
    ["symbol"] = symbol,
    ["orderId"] = string.IsNullOrWhiteSpace(orderId) ? null : orderId,
    ["clientOid"] = string.IsNullOrWhiteSpace(clientOrderId) ? null : clientOrderId
};

var bodyJson = JsonSerializer.Serialize(bodyObj, jsonOptions);

var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
var signature = Sign(apiSecret, timestamp, "POST", path, bodyJson);

using var http = new HttpClient();
using var req = new HttpRequestMessage(HttpMethod.Post, url)
{
    Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
};

req.Headers.Add("ACCESS-KEY", apiKey);
req.Headers.Add("ACCESS-SIGN", signature);
req.Headers.Add("ACCESS-PASSPHRASE", passphrase);
req.Headers.Add("ACCESS-TIMESTAMP", timestamp);
req.Headers.Add("locale", "en-US");

try
{
    using var resp = await http.SendAsync(req);
    var raw = await resp.Content.ReadAsStringAsync();

    var ok = resp.IsSuccessStatusCode;
    string? code = null;
    string? msg = null;
    JsonElement? data = null;

    try
    {
        using var respDoc = JsonDocument.Parse(raw);
        var respRoot = respDoc.RootElement;
        if (respRoot.TryGetProperty("code", out var codeEl)) code = codeEl.GetString();
        if (respRoot.TryGetProperty("msg", out var msgEl)) msg = msgEl.GetString();
        if (respRoot.TryGetProperty("data", out var dataEl)) data = dataEl.Clone();
    }
    catch
    {
        // ignore parse errors; keep raw
    }

    var result = new
    {
        success = ok && string.Equals(code, "00000", StringComparison.Ordinal),
        httpStatus = (int)resp.StatusCode,
        code,
        msg,
        request = new { symbol, orderId, clientOrderId },
        data,
        raw
    };

    Console.WriteLine("AEVATAR_TOOL_OUTPUT:" + JsonSerializer.Serialize(result, jsonOptions));
    Environment.ExitCode = result.success ? 0 : 1;
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

static string GetRequiredEnv(string key)
{
    var v = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(v))
        throw new InvalidOperationException($"Missing env var: {key}");
    return v.Trim();
}

static string GetRequiredString(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out var el))
        throw new InvalidOperationException($"Missing parameter: {name}");

    if (el.ValueKind == JsonValueKind.String)
        return el.GetString() ?? string.Empty;

    return el.GetRawText().Trim('"');
}

static string GetOptionalString(JsonElement root, string name, string? fallback)
{
    if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        return fallback ?? string.Empty;

    if (el.ValueKind == JsonValueKind.String)
        return el.GetString() ?? (fallback ?? string.Empty);

    return el.GetRawText().Trim('"');
}

static string Sign(string apiSecret, string timestamp, string method, string path, string body)
{
    var message = timestamp + method.ToUpperInvariant() + path + (body ?? "");
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    return Convert.ToBase64String(hash);
}


