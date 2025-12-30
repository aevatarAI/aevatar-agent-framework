/*aevatar_tool
{
  "name": "weex_place_order",
  "description": "Place an order on WEEX (authenticated). Reads credentials from env vars: WEEX_API_KEY, WEEX_API_SECRET, WEEX_PASSPHRASE. Optional: WEEX_BASE_URL (default https://api-spot.weex.com).",
  "category": "Custom",
  "version": "0.1.0",
  "tags": ["weex", "trade", "order", "place"],
  "requiresConfirmation": true,
  "isDangerous": true,
  "timeoutMs": 30000,
  "parameters": {
    "required": ["symbol", "side", "order_type", "quantity"],
    "items": {
      "symbol": { "type": "string", "required": true, "description": "Trading pair, e.g. BTCUSDT_SPBL" },
      "side": { "type": "string", "required": true, "enum": ["buy", "sell"], "description": "Order side" },
      "order_type": { "type": "string", "required": true, "enum": ["market", "limit"], "description": "Order type" },
      "force": { "type": "string", "required": false, "description": "Time in force, e.g. gtc/ioc/fok", "defaultValue": "gtc" },
      "quantity": { "type": "string", "required": true, "description": "Order quantity (string to preserve precision)" },
      "price": { "type": "string", "required": false, "description": "Limit price (required for limit), use \"0\" for market", "defaultValue": "0" },
      "client_order_id": { "type": "string", "required": false, "description": "Client order id for idempotency" }
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
var side = GetRequiredString(root, "side").ToLowerInvariant();
var orderType = GetRequiredString(root, "order_type").ToLowerInvariant();
var force = GetOptionalString(root, "force", "gtc");
var quantity = GetRequiredString(root, "quantity");
var price = GetOptionalString(root, "price", "0");
var clientOrderId = GetOptionalString(root, "client_order_id", null);

if (orderType == "limit" && (string.IsNullOrWhiteSpace(price) || price == "0"))
{
    throw new InvalidOperationException("For limit orders, 'price' must be provided and non-zero.");
}

var path = "/api/v2/trade/orders";
var url = new Uri(new Uri(baseUrl), path);

var bodyObj = new Dictionary<string, object?>
{
    ["symbol"] = symbol,
    ["side"] = side,
    ["orderType"] = orderType,
    ["force"] = force,
    ["quantity"] = quantity,
    ["price"] = string.IsNullOrWhiteSpace(price) ? "0" : price,
    ["clientOrderId"] = string.IsNullOrWhiteSpace(clientOrderId) ? null : clientOrderId
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
        request = new { symbol, side, orderType, force, quantity, price, clientOrderId },
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

    // Accept numbers too (model may emit number)
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


