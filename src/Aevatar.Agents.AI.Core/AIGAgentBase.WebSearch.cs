using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Optional <see cref="IHttpClientFactory"/> (injected best-effort by host DI).
    /// <para/>
    /// Used by built-in tools/providers that perform outbound HTTP requests.
    /// </summary>
    protected IHttpClientFactory? HttpClientFactory { get; set; }

    /// <summary>
    /// Optional web search provider (injected best-effort by host DI).
    /// <para/>
    /// If null, the agent may still auto-create a provider from <see cref="HostConfiguration"/>
    /// when <c>WebSearch</c> config is present.
    /// </summary>
    protected IAevatarWebSearchProvider? WebSearchProvider { get; set; }

    /// <summary>
    /// Register a web search tool when a provider is available.
    /// <para/>
    /// Best-effort:
    /// - If no provider is configured/injected, this is a no-op.
    /// - If configuration is incomplete (e.g., missing API key), this is a no-op.
    /// </summary>
    protected virtual async Task<bool> RegisterWebSearchToolBestEffortAsync(CancellationToken cancellationToken = default)
    {
        // 1) Prefer DI-provided provider
        var provider = WebSearchProvider;

        // 2) Best-effort: build from IConfiguration
        provider ??= TryCreateWebSearchProviderFromHostConfigurationBestEffort();

        if (provider == null)
            return false;

        await RegisterToolAsync(
            new WebSearchTool(provider, new LoggerAdapter<WebSearchTool>(Logger)),
            cancellationToken: cancellationToken);

        return true;
    }

    private IAevatarWebSearchProvider? TryCreateWebSearchProviderFromHostConfigurationBestEffort()
    {
        if (HostConfiguration == null)
            return null;

        // Namespaced first, then root fallback.
        var enabledText = ReadCfgValue(HostConfiguration, "enabled");
        var hasExplicitEnabled = TryParseBool(enabledText, out var enabled);
        if (hasExplicitEnabled && !enabled)
            return null;

        var providerName = (ReadCfgValue(HostConfiguration, "provider") ?? "tavily").Trim();
        var apiKey =
            (ReadCfgValue(HostConfiguration, "apiKey") ?? string.Empty).Trim();

        // Auto-enable when apiKey exists and there is no explicit enabled=false.
        if (!hasExplicitEnabled && string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Basic sanity check: avoid accidental whitespace/newlines in secrets.
        apiKey = Regex.Replace(apiKey, @"\s+", "");

        var endpoint = (ReadCfgValue(HostConfiguration, "endpoint") ?? string.Empty).Trim();
        var timeoutMs = ReadCfgInt(HostConfiguration, "timeoutMs", fallback: 15_000, min: 500, max: 300_000);
        var searchDepth = (ReadCfgValue(HostConfiguration, "searchDepth") ?? string.Empty).Trim();

        // Prefer DI-provided HttpClientFactory; fallback keeps tool functional in minimal hosts.
        var httpFactory = HttpClientFactory ?? new FallbackHttpClientFactory();

        // Provider-specific materialization
        switch (providerName.ToLowerInvariant())
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
                return new TavilyWebSearchProvider(options, httpFactory);
            }
            case "brave":
            {
                var options = new BraveWebSearchOptions
                {
                    ApiKey = apiKey,
                    Endpoint = string.IsNullOrWhiteSpace(endpoint) ? BraveWebSearchOptions.DefaultEndpoint : endpoint,
                    TimeoutMs = timeoutMs
                };
                return new BraveWebSearchProvider(options, httpFactory);
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
                return new BingWebSearchProvider(options, httpFactory);
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
                return new SerperWebSearchProvider(options, httpFactory);
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


