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
    // NOTE:
    // - Provider names are configuration keys; treat them as case-insensitive to avoid
    //   surprising "works locally but not in prod" issues due to casing differences.
    protected readonly Dictionary<string, Lazy<IAevatarLLMProvider>> Providers =
        new(StringComparer.OrdinalIgnoreCase);
    protected readonly Dictionary<string, LLMProviderConfig> ProviderConfigs;

    protected LLMProviderFactoryBase(IOptions<LLMProvidersConfig> config, ILogger logger)
    {
        Config = config.Value ?? throw new ArgumentNullException(nameof(config));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ProviderConfigs = BuildProviderConfigsWithEmbeddingFallback(Config);
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

    private static Dictionary<string, LLMProviderConfig> BuildProviderConfigsWithEmbeddingFallback(LLMProvidersConfig config)
    {
        var dict = new Dictionary<string, LLMProviderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in config.Providers)
        {
            var name = kv.Key;
            var src = kv.Value;
            if (src == null)
                continue;

            var copy = CloneProviderConfig(src);

            // Make provider name stable even if config omitted Name.
            if (string.IsNullOrWhiteSpace(copy.Name))
                copy.Name = name;

            // Embeddings:
            // - If provider doesn't define embeddings -> inherit global embeddings.
            // - If provider defines embeddings -> partial-merge missing fields from global embeddings.
            if (config.Embeddings != null)
            {
                if (copy.Embeddings == null)
                {
                    copy.Embeddings = CloneEmbeddingConfig(config.Embeddings);
                }
                else
                {
                    copy.Embeddings = MergeEmbeddingConfig(config.Embeddings, copy.Embeddings);
                }
            }

            dict[name] = copy;
        }

        return dict;
    }

    private static LLMProviderConfig CloneProviderConfig(LLMProviderConfig src)
    {
        return new LLMProviderConfig
        {
            Name = src.Name,
            ProviderType = src.ProviderType,
            ApiKey = src.ApiKey,
            Model = src.Model,
            Endpoint = src.Endpoint,
            DeploymentName = src.DeploymentName,
            Temperature = src.Temperature,
            MaxTokens = src.MaxTokens,
            TimeoutMilliseconds = src.TimeoutMilliseconds,
            EnableStreaming = src.EnableStreaming,
            ProviderSpecificSettings = src.ProviderSpecificSettings != null
                ? new Dictionary<string, object>(src.ProviderSpecificSettings)
                : new Dictionary<string, object>(),
            Embeddings = src.Embeddings != null ? CloneEmbeddingConfig(src.Embeddings) : null
        };
    }

    private static LLMEmbeddingConfig CloneEmbeddingConfig(LLMEmbeddingConfig src)
    {
        return new LLMEmbeddingConfig
        {
            ProviderType = src.ProviderType,
            Model = src.Model,
            DeploymentName = src.DeploymentName,
            Endpoint = src.Endpoint,
            ApiKey = src.ApiKey,
            Dimensions = src.Dimensions,
            ProviderSpecificSettings = src.ProviderSpecificSettings != null
                ? new Dictionary<string, object>(src.ProviderSpecificSettings)
                : new Dictionary<string, object>()
        };
    }

    private static LLMEmbeddingConfig MergeEmbeddingConfig(LLMEmbeddingConfig global, LLMEmbeddingConfig provider)
    {
        // Provider overrides explicitly set fields; missing fields fall back to global.
        var merged = CloneEmbeddingConfig(provider);

        if (string.IsNullOrWhiteSpace(merged.ProviderType))
            merged.ProviderType = global.ProviderType;
        if (string.IsNullOrWhiteSpace(merged.Model))
            merged.Model = global.Model;
        if (string.IsNullOrWhiteSpace(merged.DeploymentName))
            merged.DeploymentName = global.DeploymentName;
        if (string.IsNullOrWhiteSpace(merged.Endpoint))
            merged.Endpoint = global.Endpoint;
        if (string.IsNullOrWhiteSpace(merged.ApiKey))
            merged.ApiKey = global.ApiKey;
        if (!merged.Dimensions.HasValue)
            merged.Dimensions = global.Dimensions;

        // ProviderSpecificSettings: merge global -> provider (provider wins on key conflicts).
        var mergedSettings = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (global.ProviderSpecificSettings != null)
        {
            foreach (var kv in global.ProviderSpecificSettings)
                mergedSettings[kv.Key] = kv.Value;
        }
        if (provider.ProviderSpecificSettings != null)
        {
            foreach (var kv in provider.ProviderSpecificSettings)
                mergedSettings[kv.Key] = kv.Value;
        }
        merged.ProviderSpecificSettings = mergedSettings;

        return merged;
    }
}