using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Providers;

// ============================================================
//  CompositeLLMProviderFactory
//
//  中文说明：
//  - 修复框架层“只能注入一个 ILLMProviderFactory”的缺口
//  - 当应用在 DI 里注册了多个 ILLMProviderFactory（例如 MEAI + LLMTornado），
//    Injector 会把它们组合成一个工厂注入给 AIGAgentBase。
//
//  设计原则：
//  - 不改变现有接口，不要求应用改代码（只要它注册了多个 factory）
//  - 路由策略：按 ProviderType 做最小、确定性的分流
//    - Claude/Gemini/本地模型等 → 优先 “Tornado” 工厂（按类型名匹配）
//    - 其它（尤其 OpenAI-compatible）→ 优先 “MEAI” 工厂（按类型名匹配）
// ============================================================
public sealed class CompositeLLMProviderFactory : ILLMProviderFactory
{
    private readonly IReadOnlyList<ILLMProviderFactory> _factories;
    private readonly ILogger? _logger;

    public CompositeLLMProviderFactory(IReadOnlyList<ILLMProviderFactory> factories, ILogger? logger = null)
    {
        _factories = factories ?? throw new ArgumentNullException(nameof(factories));
        if (_factories.Count == 0)
            throw new ArgumentException("At least one ILLMProviderFactory is required.", nameof(factories));
        _logger = logger;
    }

    public IAevatarLLMProvider GetProvider(string providerName)
    {
        foreach (var f in _factories)
        {
            if (f.HasProvider(providerName))
                return f.GetProvider(providerName);
        }

        // Fallback: let the first factory throw a meaningful error message.
        return _factories[0].GetProvider(providerName);
    }

    public IAevatarLLMProvider GetDefaultProvider()
        => _factories[0].GetDefaultProvider();

    public IReadOnlyList<string> GetAvailableProviderNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _factories)
        {
            foreach (var n in f.GetAvailableProviderNames())
                set.Add(n);
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool HasProvider(string providerName)
        => _factories.Any(f => f.HasProvider(providerName));

    public LLMProviderConfig GetProviderConfig(string providerName)
    {
        foreach (var f in _factories)
        {
            if (f.HasProvider(providerName))
                return f.GetProviderConfig(providerName);
        }

        // Fallback: let the first factory throw.
        return _factories[0].GetProviderConfig(providerName);
    }

    public LLMProviderConfig GetDefaultProviderConfig()
        => _factories[0].GetDefaultProviderConfig();

    public IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default)
    {
        if (providerConfig == null)
            throw new ArgumentNullException(nameof(providerConfig));

        var selected = SelectFactory(providerConfig);
        _logger?.LogDebug(
            "CompositeLLMProviderFactory routing: ProviderName={Name}, ProviderType={Type}, Factory={FactoryType}",
            providerConfig.Name ?? string.Empty,
            providerConfig.ProviderType ?? string.Empty,
            selected.GetType().FullName ?? selected.GetType().Name);

        // IMPORTANT:
        // - We do NOT fallback to another factory on exceptions.
        // - If routing is wrong, fail fast; otherwise bugs become silent & nondeterministic.
        return selected.CreateProvider(providerConfig, cancellationToken);
    }

    public Task<IAevatarLLMProvider> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
        => Task.FromResult(GetProvider(providerName));

    public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetDefaultProvider());

    private ILLMProviderFactory SelectFactory(LLMProviderConfig providerConfig)
    {
        // Default: first factory wins (keeps legacy behavior stable).
        if (_factories.Count == 1)
            return _factories[0];

        var type = (providerConfig.ProviderType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Length == 0)
            return PreferByTypeNameContains("meai") ?? _factories[0];

        // ------------------------------------------------------------
        // Explicit "non-openai" types should prefer Tornado-style factories.
        // ------------------------------------------------------------
        var preferTornado = type is
            "anthropic" or "claude" or
            "google" or "gemini" or
            "cohere" or
            "groq" or
            "ollama" or "vllm" or "localai" or
            "claude_agent_sdk" or "claude-agent-sdk" or
            "llmtornado" or "tornado";

        if (preferTornado)
        {
            return PreferByTypeNameContains("llmtornado") ??
                   PreferByTypeNameContains("tornado") ??
                   _factories[0];
        }

        // Azure OpenAI is handled better by MEAI.
        if (type is "azureopenai" or "azure_openai" or "azure")
        {
            return PreferByTypeNameContains("meai") ?? _factories[0];
        }

        // OpenAI-compatible: keep MEAI as default if available.
        return PreferByTypeNameContains("meai") ?? _factories[0];
    }

    private ILLMProviderFactory? PreferByTypeNameContains(string needleLower)
    {
        foreach (var f in _factories)
        {
            var n = (f.GetType().FullName ?? f.GetType().Name).ToLowerInvariant();
            if (n.Contains(needleLower, StringComparison.Ordinal))
                return f;
        }
        return null;
    }
}


