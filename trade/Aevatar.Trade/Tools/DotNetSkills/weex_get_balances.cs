/*aevatar_tool
{
  "name": "weex_get_balances",
  "description": "Fetch account balances from WEEX (authenticated). Reads credentials from env vars: WEEX_API_KEY, WEEX_API_SECRET, WEEX_PASSPHRASE. Optional: WEEX_BASE_URL (default https://api-spot.weex.com).",
  "category": "Custom",
  "version": "0.1.0",
  "tags": ["weex", "account", "balance", "trade"],
  "requiresConfirmation": false,
  "isDangerous": false,
  "timeoutMs": 20000,
  "parameters": {
    "items": {},
    "required": []
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

var path = "/api/v2/account/balance";
var url = new Uri(new Uri(baseUrl), path);

var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
var signature = Sign(apiSecret, timestamp, "GET", path, body: "");

using var http = new HttpClient();
using var req = new HttpRequestMessage(HttpMethod.Get, url);
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
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var codeEl)) code = codeEl.GetString();
        if (root.TryGetProperty("msg", out var msgEl)) msg = msgEl.GetString();
        if (root.TryGetProperty("data", out var dataEl)) data = dataEl.Clone();
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

static string Sign(string apiSecret, string timestamp, string method, string path, string body)
{
    var message = timestamp + method.ToUpperInvariant() + path + (body ?? "");
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    return Convert.ToBase64String(hash);
}


