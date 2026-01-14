using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn.WebSearch;

/// <summary>
/// WebSearch provider factory (best-effort).
///
/// 中文 + ASCII:
/// - 只负责「从 IConfiguration + 环境变量」解析配置并物化 provider
/// - 不负责注册 tool（tool 注册由 AIGAgentBase 负责）
/// - 失败语义：返回 null（best-effort），不抛出异常
/// </summary>
internal static class WebSearchProviderFactory
{
    internal static IAevatarWebSearchProvider? TryCreateFromConfigurationBestEffort(
        IConfiguration? configuration,
        IHttpClientFactory? httpClientFactory)
    {
        if (configuration == null)
            return null;

        // Namespaced first, then root fallback.
        var enabledText = ReadCfgValue(configuration, "enabled");
        var hasExplicitEnabled = TryParseBool(enabledText, out var enabled);
        if (hasExplicitEnabled && !enabled)
            return null;

        var providerName = (ReadCfgValue(configuration, "provider") ?? "tavily").Trim();
        var apiKey = (ReadCfgValue(configuration, "apiKey") ?? string.Empty).Trim();

        // Auto-enable when apiKey exists and there is no explicit enabled=false.
        if (!hasExplicitEnabled && string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Basic sanity check: avoid accidental whitespace/newlines in secrets.
        apiKey = Regex.Replace(apiKey, @"\s+", "");

        var endpoint = (ReadCfgValue(configuration, "endpoint") ?? string.Empty).Trim();
        var timeoutMs = ReadCfgInt(configuration, "timeoutMs", fallback: 15_000, min: 500, max: 300_000);
        var searchDepth = (ReadCfgValue(configuration, "searchDepth") ?? string.Empty).Trim();

        // Prefer DI-provided HttpClientFactory; fallback keeps tool functional in minimal hosts.
        var httpFactory = httpClientFactory ?? new FallbackHttpClientFactory();

        return MaterializeProvider(providerName, apiKey, endpoint, timeoutMs, searchDepth, httpFactory);
    }

    private static IAevatarWebSearchProvider? MaterializeProvider(
        string providerName,
        string apiKey,
        string endpoint,
        int timeoutMs,
        string searchDepth,
        IHttpClientFactory httpClientFactory)
    {
        switch (providerName.Trim().ToLowerInvariant())
        {
            case "tavily":
            {
                var options = new TavilyWebSearchOptions
                {
                    ApiKey = apiKey,
                    Endpoint = string.IsNullOrWhiteSpace(endpoint) ? TavilyWebSearchOptions.DefaultEndpoint : endpoint,
                    TimeoutMs = timeoutMs,
                    SearchDepth = string.IsNullOrWhiteSpace(searchDepth) ? "basic" : searchDepth
                };
                return new TavilyWebSearchProvider(options, httpClientFactory);
            }
            case "brave":
            {
                var options = new BraveWebSearchOptions
                {
                    ApiKey = apiKey,
                    Endpoint = string.IsNullOrWhiteSpace(endpoint) ? BraveWebSearchOptions.DefaultEndpoint : endpoint,
                    TimeoutMs = timeoutMs
                };
                return new BraveWebSearchProvider(options, httpClientFactory);
            }
            case "bing":
            case "azure-bing":
            {
                var options = new BingWebSearchOptions
                {
                    ApiKey = apiKey,
                    Endpoint = string.IsNullOrWhiteSpace(endpoint) ? BingWebSearchOptions.DefaultEndpoint : endpoint,
                    TimeoutMs = timeoutMs
                };
                return new BingWebSearchProvider(options, httpClientFactory);
            }
            case "serper":
            case "google-serper":
            {
                var options = new SerperWebSearchOptions
                {
                    ApiKey = apiKey,
                    Endpoint = string.IsNullOrWhiteSpace(endpoint) ? SerperWebSearchOptions.DefaultEndpoint : endpoint,
                    TimeoutMs = timeoutMs
                };
                return new SerperWebSearchProvider(options, httpClientFactory);
            }
            default:
                // Unknown provider => no tool.
                return null;
        }
    }

    private static string? ReadCfgValue(IConfiguration cfg, string key)
    {
        // 1) Namespaced: Aevatar:Tools:WebSearch:*
        var v = cfg[$"Aevatar:Tools:WebSearch:{key}"];
        if (!string.IsNullOrWhiteSpace(v))
            return v;

        // 2) Root: WebSearch:*
        v = cfg[$"WebSearch:{key}"];
        if (!string.IsNullOrWhiteSpace(v))
            return v;

        // 3) Env-style fallback (optional)
        // This allows running without appsettings: export AEVATAR_WEBSEARCH_API_KEY=...
        if (string.Equals(key, "apiKey", StringComparison.OrdinalIgnoreCase))
        {
            v = Environment.GetEnvironmentVariable("AEVATAR_WEBSEARCH_API_KEY");
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        if (string.Equals(key, "provider", StringComparison.OrdinalIgnoreCase))
        {
            v = Environment.GetEnvironmentVariable("AEVATAR_WEBSEARCH_PROVIDER");
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        if (string.Equals(key, "endpoint", StringComparison.OrdinalIgnoreCase))
        {
            v = Environment.GetEnvironmentVariable("AEVATAR_WEBSEARCH_ENDPOINT");
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        if (string.Equals(key, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            v = Environment.GetEnvironmentVariable("AEVATAR_WEBSEARCH_ENABLED");
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        if (string.Equals(key, "timeoutMs", StringComparison.OrdinalIgnoreCase))
        {
            v = Environment.GetEnvironmentVariable("AEVATAR_WEBSEARCH_TIMEOUT_MS");
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }

    private static int ReadCfgInt(IConfiguration cfg, string key, int fallback, int min, int max)
    {
        var s = ReadCfgValue(cfg, key);
        if (!int.TryParse(s, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    private static bool TryParseBool(string? s, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var t = s.Trim();
        if (bool.TryParse(t, out value))
            return true;

        // Common numeric variants
        if (string.Equals(t, "1", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(t, "0", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }
}


