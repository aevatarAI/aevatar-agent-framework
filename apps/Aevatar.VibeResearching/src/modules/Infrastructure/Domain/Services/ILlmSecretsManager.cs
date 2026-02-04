namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for LLM secrets and provider configuration management.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface ILlmSecretsManager
{
    /// <summary>
    /// Gets the status of LLM provider configurations.
    /// </summary>
    Task<object> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Gets available LLM provider catalog.
    /// </summary>
    Task<object> GetProviderCatalogAsync(CancellationToken ct);

    /// <summary>
    /// Gets provider profiles (configurations).
    /// </summary>
    Task<object> GetProviderProfilesAsync(CancellationToken ct);

    /// <summary>
    /// Updates provider secrets/configuration.
    /// </summary>
    Task UpdateProviderAsync(string providerName, object configuration, CancellationToken ct);

    /// <summary>
    /// Removes provider configuration.
    /// </summary>
    Task RemoveProviderAsync(string providerName, CancellationToken ct);

    /// <summary>
    /// Resolves the appropriate provider for a given model or request.
    /// </summary>
    Task<object> ResolveProviderAsync(string modelOrRequest, CancellationToken ct);

    /// <summary>
    /// Gets available provider types.
    /// </summary>
    Task<IReadOnlyList<object>> GetProviderTypesAsync(CancellationToken ct);

    /// <summary>
    /// Gets configured provider instances.
    /// </summary>
    Task<IReadOnlyList<object>> GetProviderInstancesAsync(CancellationToken ct);

    /// <summary>
    /// Gets resolved provider configuration with public keys only.
    /// </summary>
    Task<object> GetResolvedProviderAsync(string providerName, CancellationToken ct);

    /// <summary>
    /// Sets an API key for a provider.
    /// </summary>
    Task SetApiKeyAsync(object request, CancellationToken ct);

    /// <summary>
    /// Sets the default provider.
    /// </summary>
    Task SetDefaultProviderAsync(object request, CancellationToken ct);

    /// <summary>
    /// Upserts a provider instance configuration.
    /// </summary>
    Task UpsertProviderAsync(object request, CancellationToken ct);

    /// <summary>
    /// Probes an LLM endpoint to verify connectivity.
    /// </summary>
    Task<object> ProbeLlmAsync(object request, CancellationToken ct);

    /// <summary>
    /// Upserts embeddings provider configuration.
    /// </summary>
    Task UpsertEmbeddingsAsync(object request, CancellationToken ct);

    /// <summary>
    /// Gets list of trashed API keys.
    /// </summary>
    Task<IReadOnlyList<object>> GetTrashedKeysAsync(CancellationToken ct);

    /// <summary>
    /// Restores a trashed API key.
    /// </summary>
    Task RestoreTrashedKeyAsync(string providerName, CancellationToken ct);

    /// <summary>
    /// Sets a secret value.
    /// </summary>
    Task SetSecretAsync(object request, CancellationToken ct);

    /// <summary>
    /// Removes a secret.
    /// </summary>
    Task RemoveSecretAsync(object request, CancellationToken ct);
}
