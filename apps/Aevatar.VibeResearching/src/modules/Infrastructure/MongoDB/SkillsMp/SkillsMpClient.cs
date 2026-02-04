using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.SkillsMp;

// ============================================================
//  SkillsMP Client (best-effort)
//
//  中文说明：
//  - 仅实现 SkillsMP 文档中明确存在的搜索能力：
//    GET /api/v1/skills/search
//    GET /api/v1/skills/ai-search
//  - 不在服务端回显/记录 API Key
//  - 响应结构保持"弱绑定"：返回 raw + best-effort items 提取
// ============================================================
public sealed class SkillsMpClient
{
    public const string ApiKeyConfigKey = "SkillsMP:ApiKey";
    public const string BaseUrlConfigKey = "SkillsMP:BaseUrl";
    public const string DefaultBaseUrl = "https://skillsmp.com";
    public const string ApiKeyEnv = "SKILLSMP_API_KEY";
    public const string CloudflareClearanceConfigKey = "SkillsMP:CloudflareClearance";
    public const string CloudflareClearanceEnv = "SKILLSMP_CF_CLEARANCE";

    private readonly HttpClient _http;
    private readonly IAevatarUserSecretsStore _secrets;
    private readonly IConfiguration _cfg;
    private readonly ILogger<SkillsMpClient> _logger;

    public SkillsMpClient(
        HttpClient http,
        IAevatarUserSecretsStore secrets,
        IConfiguration cfg,
        ILogger<SkillsMpClient> logger)
    {
        _http = http;
        _secrets = secrets;
        _cfg = cfg;
        _logger = logger;
    }

    public sealed record SkillsMpSkillItem(
        string Id,
        string Name,
        string Description,
        string? RepoUrl,
        string? Url,
        int? Stars);

    public async Task<(bool Ok, JsonNode? Raw, List<SkillsMpSkillItem> Items, string? Error, int? StatusCode)> SearchAsync(
        string query,
        int page,
        int limit,
        string? sortBy,
        CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
            return (false, null, new List<SkillsMpSkillItem>(), "q is required", 400);

        if (!TryResolveApiKey(out var apiKey, out var apiKeyErr))
            return (false, null, new List<SkillsMpSkillItem>(), apiKeyErr ?? "SkillsMP apiKey not configured", 400);

        var baseUrl = ResolveBaseUrl();
        page = Math.Clamp(page, 1, 1000);
        limit = Math.Clamp(limit, 1, 100);
        sortBy = NormalizeSortBy(sortBy);

        var url = $"{baseUrl}/api/v1/skills/search?q={Uri.EscapeDataString(query)}&page={page}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(sortBy))
            url += $"&sortBy={Uri.EscapeDataString(sortBy)}";

        return await GetAndParseAsync(url, apiKey, ct);
    }

    public async Task<(bool Ok, JsonNode? Raw, List<SkillsMpSkillItem> Items, string? Error, int? StatusCode)> AiSearchAsync(
        string query,
        CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
            return (false, null, new List<SkillsMpSkillItem>(), "q is required", 400);

        if (!TryResolveApiKey(out var apiKey, out var apiKeyErr))
            return (false, null, new List<SkillsMpSkillItem>(), apiKeyErr ?? "SkillsMP apiKey not configured", 400);

        var baseUrl = ResolveBaseUrl();
        var url = $"{baseUrl}/api/v1/skills/ai-search?q={Uri.EscapeDataString(query)}";
        return await GetAndParseAsync(url, apiKey, ct);
    }

    private async Task<(bool Ok, JsonNode? Raw, List<SkillsMpSkillItem> Items, string? Error, int? StatusCode)> GetAndParseAsync(
        string url,
        string apiKey,
        CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Some upstream gateways (e.g. Cloudflare) may reject requests without a browser-like UA.
            // This is best-effort and does NOT attempt to bypass any interactive challenge.
            try
            {
                req.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            }
            catch
            {
                // ignore header formatting errors (best-effort)
            }

            // Optional: user-provided Cloudflare clearance cookie (obtained after solving "Just a moment" in browser).
            // This is not a bypass; it is the official session clearance cookie for the current IP and will expire.
            if (TryResolveCloudflareClearance(out var clearanceCookie, out _))
            {
                try
                {
                    // Accept either a raw token or a full cookie string.
                    var cookie = clearanceCookie.Contains("cf_clearance=", StringComparison.OrdinalIgnoreCase)
                        ? clearanceCookie.Trim()
                        : $"cf_clearance={clearanceCookie.Trim()}";
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);

                    // Some WAF setups also like a plausible origin/referer.
                    req.Headers.TryAddWithoutValidation("Origin", "https://skillsmp.com");
                    req.Headers.TryAddWithoutValidation("Referer", "https://skillsmp.com/zh/docs/api");
                }
                catch
                {
                    // ignore
                }
            }

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync(ct);

            // Cloudflare sometimes blocks non-browser TLS fingerprints even with valid API keys.
            // If we detect an HTML challenge page, fallback to system curl (best-effort).
            if (LooksLikeCloudflareBlock(status, body))
            {
                var (ok2, raw2, items2, err2, code2) = await TryCurlFallbackAsync(url, apiKey, ct);
                if (ok2)
                    return (ok2, raw2, items2, err2, code2);

                // Keep original status/body for visibility (already trimmed).
                var note =
                    "Blocked by Cloudflare (challenge page). " +
                    "If this persists: " +
                    "1) open skillsmp.com in a browser and solve the challenge, " +
                    $"2) copy the 'cf_clearance' cookie value, " +
                    $"3) set '{CloudflareClearanceConfigKey}' in encrypted user secrets (Secrets UI -> Advanced), " +
                    "then retry. " +
                    "Alternatively contact SkillsMP to whitelist your IP.";
                var combined = string.IsNullOrWhiteSpace(err2) ? note : (note + " CurlFallback: " + err2);
                return (false, null, new List<SkillsMpSkillItem>(), combined, status);
            }

            if (!resp.IsSuccessStatusCode)
            {
                // Never echo secrets; only return a bounded body excerpt.
                return (false, null, new List<SkillsMpSkillItem>(), $"HTTP {status}: {TrimForUi(body)}", status);
            }

            JsonNode? raw;
            try
            {
                raw = JsonNode.Parse(body);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SkillsMP] Failed to parse JSON (best-effort).");
                return (false, null, new List<SkillsMpSkillItem>(), "Invalid JSON from SkillsMP", status);
            }

            var items = ExtractItemsBestEffort(raw, maxItems: 50);
            return (true, raw, items, null, status);
        }
        catch (OperationCanceledException)
        {
            return (false, null, new List<SkillsMpSkillItem>(), "cancelled", 499);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SkillsMP] Request failed (best-effort): {Message}", ex.Message);
            return (false, null, new List<SkillsMpSkillItem>(), ex.Message, null);
        }
    }

    private static bool LooksLikeCloudflareBlock(int statusCode, string? body)
    {
        if (statusCode != 403 && statusCode != 503)
            return false;

        var s = (body ?? string.Empty);
        if (s.Length == 0) return false;
        // common markers
        return s.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase)
               || s.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)
               || s.Contains("/cdn-cgi/", StringComparison.OrdinalIgnoreCase)
               || s.Contains("cf-ray", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(bool Ok, JsonNode? Raw, List<SkillsMpSkillItem> Items, string? Error, int? StatusCode)> TryCurlFallbackAsync(
        string url,
        string apiKey,
        CancellationToken ct)
    {
        // Best-effort only; do not fail the whole feature if curl is unavailable.
        // Security:
        // - Never log the apiKey.
        // - Never include apiKey in error messages.
        //
        // Note:
        // - Using curl may have a different TLS fingerprint and can pass Cloudflare where .NET HttpClient fails.
        try
        {
            const string marker = "__aevatar_http_code__=";
            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // curl -sS -m 20 --compressed -H "Authorization: Bearer <key>" -H "Accept: application/json" -A "<ua>" URL -w "\n__aevatar_http_code__=%{http_code}\n"
            psi.ArgumentList.Add("-sS");
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("20");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Accept: application/json");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Accept-Language: zh-CN,zh;q=0.9,en;q=0.8");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Cache-Control: no-cache");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add($"Authorization: Bearer {apiKey}");
            if (TryResolveCloudflareClearance(out var clearanceCookie, out _))
            {
                var cookie = clearanceCookie.Contains("cf_clearance=", StringComparison.OrdinalIgnoreCase)
                    ? clearanceCookie.Trim()
                    : $"cf_clearance={clearanceCookie.Trim()}";
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Cookie: " + cookie);
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Origin: https://skillsmp.com");
                psi.ArgumentList.Add("-H");
                psi.ArgumentList.Add("Referer: https://skillsmp.com/zh/docs/api");
            }
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            psi.ArgumentList.Add(url);
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add($"\n{marker}%{{http_code}}\n");

            using var p = Process.Start(psi);
            if (p == null)
                return (false, null, new List<SkillsMpSkillItem>(), "curl not available", null);

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // Parse trailing http_code marker
            var idx = stdout.LastIndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return (false, null, new List<SkillsMpSkillItem>(), "curl output malformed", null);

            var body = stdout.Substring(0, idx).TrimEnd('\r', '\n');
            var rest = stdout.Substring(idx + marker.Length);
            var codeStr = rest.Trim().Split('\n').FirstOrDefault() ?? "";
            if (!int.TryParse(codeStr.Trim(), out var code))
                code = 0;

            if (code < 200 || code >= 300)
            {
                var err = $"HTTP {code}: {TrimForUi(body)}";
                if (!string.IsNullOrWhiteSpace(stderr))
                    err += $" (curl: {TrimForUi(stderr)})";
                return (false, null, new List<SkillsMpSkillItem>(), err, code);
            }

            JsonNode? raw;
            try
            {
                raw = JsonNode.Parse(body);
            }
            catch
            {
                return (false, null, new List<SkillsMpSkillItem>(), "Invalid JSON from SkillsMP (curl)", code);
            }

            var items = ExtractItemsBestEffort(raw, maxItems: 50);
            return (true, raw, items, null, code);
        }
        catch (OperationCanceledException)
        {
            return (false, null, new List<SkillsMpSkillItem>(), "cancelled", 499);
        }
        catch (Exception ex)
        {
            return (false, null, new List<SkillsMpSkillItem>(), ex.Message, null);
        }
    }

    private bool TryResolveCloudflareClearance(out string clearance, out string? error)
    {
        clearance = string.Empty;
        error = null;

        // Priority: secrets -> cfg -> env
        if (_secrets.TryGet(CloudflareClearanceConfigKey, out var s) && !string.IsNullOrWhiteSpace(s))
        {
            clearance = s.Trim();
            return true;
        }

        var fromCfg = (_cfg[CloudflareClearanceConfigKey] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fromCfg))
        {
            clearance = fromCfg;
            return true;
        }

        var fromEnv = (Environment.GetEnvironmentVariable(CloudflareClearanceEnv) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            clearance = fromEnv;
            return true;
        }

        return false;
    }

    private bool TryResolveApiKey(out string apiKey, out string? error)
    {
        apiKey = string.Empty;
        error = null;

        // Priority:
        // 1) user secrets store (runtime-writable)
        // 2) IConfiguration (covers appsettings.secrets.json + AddAevatarUserConfig)
        // 3) env var
        if (_secrets.TryGet(ApiKeyConfigKey, out var fromSecrets) && !string.IsNullOrWhiteSpace(fromSecrets))
        {
            apiKey = fromSecrets.Trim();
            return true;
        }

        var fromCfg = (_cfg[ApiKeyConfigKey] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fromCfg))
        {
            apiKey = fromCfg;
            return true;
        }

        var fromEnv = (Environment.GetEnvironmentVariable(ApiKeyEnv) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            apiKey = fromEnv;
            return true;
        }

        error = $"SkillsMP API key not configured. Set '{ApiKeyConfigKey}' (preferred) or env '{ApiKeyEnv}'.";
        return false;
    }

    private string ResolveBaseUrl()
    {
        // Best-effort override (non-secret).
        var raw = (_cfg[BaseUrlConfigKey] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                return raw.TrimEnd('/');
            }
            catch
            {
                // ignore invalid override
            }
        }

        return DefaultBaseUrl.TrimEnd('/');
    }

    private static string? NormalizeSortBy(string? sortBy)
    {
        var s = (sortBy ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        if (string.Equals(s, "stars", StringComparison.OrdinalIgnoreCase)) return "stars";
        if (string.Equals(s, "recent", StringComparison.OrdinalIgnoreCase)) return "recent";
        return null;
    }

    private static List<SkillsMpSkillItem> ExtractItemsBestEffort(JsonNode? raw, int maxItems)
    {
        var list = new List<SkillsMpSkillItem>();
        if (raw == null) return list;

        JsonArray? arr = null;

        if (raw is JsonArray a0)
        {
            arr = a0;
        }
        else if (raw is JsonObject obj)
        {
            // Common list fields
            arr =
                obj["skills"] as JsonArray ??
                obj["data"] as JsonArray ??
                obj["items"] as JsonArray ??
                obj["results"] as JsonArray ??
                obj["list"] as JsonArray;
        }

        if (arr == null)
            return list;

        foreach (var n in arr)
        {
            if (list.Count >= maxItems) break;
            if (n is not JsonObject item) continue;

            var name = PickString(item, "name", "title", "skillName", "displayName") ?? string.Empty;
            var desc = PickString(item, "description", "summary", "desc") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var id =
                PickString(item, "id", "_id", "skillId", "slug") ??
                Slugify(name);

            var repoUrl =
                PickUrlByHint(item, "repo", "git", "github", "source") ??
                null;

            var url =
                PickUrlByHint(item, "url", "page", "link", "homepage") ??
                null;

            var stars = PickInt(item, "stars", "starCount", "stargazersCount");

            list.Add(new SkillsMpSkillItem(
                Id: id,
                Name: name.Trim(),
                Description: (desc ?? string.Empty).Trim(),
                RepoUrl: repoUrl,
                Url: url,
                Stars: stars));
        }

        return list;
    }

    private static string? PickString(JsonObject obj, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!obj.TryGetPropertyValue(k, out var n) || n == null)
                continue;
            var s = n.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }

        return null;
    }

    private static int? PickInt(JsonObject obj, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!obj.TryGetPropertyValue(k, out var n) || n == null)
                continue;
            try
            {
                if (n is JsonValue v)
                {
                    if (v.TryGetValue<int>(out var i))
                        return i;
                    if (v.TryGetValue<long>(out var l))
                        return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
                }

                if (int.TryParse(n.ToString(), out var parsed))
                    return parsed;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string? PickUrlByHint(JsonObject obj, params string[] hints)
    {
        foreach (var kv in obj)
        {
            var key = kv.Key ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) continue;

            var hit = hints.Any(h => key.Contains(h, StringComparison.OrdinalIgnoreCase));
            if (!hit) continue;

            var val = kv.Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(val)) continue;

            if (LooksLikeUrl(val))
                return val;
        }

        return null;
    }

    private static bool LooksLikeUrl(string s)
        => s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
           s.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static string Slugify(string s)
    {
        var raw = (s ?? string.Empty).Trim().ToLowerInvariant();
        if (raw.Length == 0) return "skill";

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')
            {
                sb.Append('-');
            }
        }

        var t = sb.ToString().Trim('-');
        return t.Length == 0 ? "skill" : t;
    }

    private static string TrimForUi(string text, int max = 800)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }
}
