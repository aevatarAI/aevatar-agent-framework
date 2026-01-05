using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Abstractions.Providers;

/// <summary>
/// LLM Provider Factory abstract base class, provides common implementation
/// </summary>
public abstract class LLMProviderFactoryBase : ILLMProviderFactory
{
    protected readonly LLMProvidersConfig Config;
    protected readonly ILogger Logger;
    protected readonly Dictionary<string, Lazy<IAevatarLLMProvider>> Providers = new();
    protected readonly Dictionary<string, LLMProviderConfig> ProviderConfigs;

    protected LLMProviderFactoryBase(IOptions<LLMProvidersConfig> config, ILogger logger)
    {
        Config = config.Value ?? throw new ArgumentNullException(nameof(config));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ProviderConfigs = new Dictionary<string, LLMProviderConfig>(Config.Providers);
        RegisterProviders();
    }

    public IAevatarLLMProvider GetProvider(string providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            throw new ArgumentNullException(nameof(providerName));

        if (Providers.TryGetValue(providerName, out var provider))
            return provider.Value;

        throw new KeyNotFoundException(
            $"Provider '{providerName}' not found. Available providers: {string.Join(", ", GetAvailableProviderNames())}");
    }

    public IAevatarLLMProvider GetDefaultProvider()
    {
        return GetProvider(Config.Default);
    }

    public IReadOnlyList<string> GetAvailableProviderNames()
    {
        return new List<string>(Providers.Keys).AsReadOnly();
    }

    public bool HasProvider(string providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            return false;

        return Providers.ContainsKey(providerName);
    }

    public LLMProviderConfig GetProviderConfig(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentNullException(nameof(providerName));

        if (ProviderConfigs.TryGetValue(providerName, out var config))
            return config;

        throw new KeyNotFoundException(
            $"Provider config '{providerName}' not found. Available providers: {string.Join(", ", ProviderConfigs.Keys)}");
    }

    public LLMProviderConfig GetDefaultProviderConfig()
    {
        return GetProviderConfig(Config.Default);
    }

    public abstract IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default);

    public async Task<IAevatarLLMProvider> GetProviderAsync(string providerName,
        CancellationToken cancellationToken = default)
    {
        // Async version can add async initialization logic here
        return await Task.FromResult(GetProvider(providerName));
    }

    public async Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
    {
        return await GetProviderAsync(Config.Default, cancellationToken);
    }

    protected virtual void RegisterProviders()
    {
        foreach (var config in ProviderConfigs)
        {
            Providers[config.Key] = new Lazy<IAevatarLLMProvider>(() => CreateProvider(config.Value));
        }
    }
}