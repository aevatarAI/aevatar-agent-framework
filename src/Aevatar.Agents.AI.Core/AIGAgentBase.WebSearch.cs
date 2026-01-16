using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn.WebSearch;
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
        provider ??= WebSearchProviderFactory.TryCreateFromConfigurationBestEffort(HostConfiguration, HttpClientFactory);

        if (provider == null)
            return false;

        await RegisterToolAsync(
            new WebSearchTool(provider, new LoggerAdapter<WebSearchTool>(Logger)),
            cancellationToken: cancellationToken);

        return true;
    }

    // WebSearchProviderFactory carries the best-effort config parsing + provider materialization,
    // keeping AIGAgentBase focused on tool registration orchestration.
}


