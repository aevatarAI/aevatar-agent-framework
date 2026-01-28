using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for LLM provider secrets and configuration management.
/// </summary>
public interface ILlmSecretsAppService : IApplicationService
{
    /// <summary>
    /// Gets all configured provider types.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of provider types</returns>
    Task<IReadOnlyList<ProviderTypeItem>> GetProviderTypesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all configured provider instances.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of provider instances</returns>
    Task<IReadOnlyList<ProviderInstanceItem>> GetProviderInstancesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the resolved configuration for a specific provider.
    /// </summary>
    /// <param name="providerName">Provider name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Resolved provider configuration (public view)</returns>
    Task<ResolvedProviderPublic> GetResolvedProviderAsync(string providerName, CancellationToken ct = default);

    /// <summary>
    /// Sets an API key for a provider.
    /// </summary>
    /// <param name="request">API key request</param>
    /// <param name="ct">Cancellation token</param>
    Task SetApiKeyAsync(SetLlmApiKeyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sets the default LLM provider.
    /// </summary>
    /// <param name="request">Default provider request</param>
    /// <param name="ct">Cancellation token</param>
    Task SetDefaultProviderAsync(SetLlmDefaultRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates an LLM provider instance.
    /// </summary>
    /// <param name="request">Provider instance configuration</param>
    /// <param name="ct">Cancellation token</param>
    Task UpsertProviderAsync(UpsertLlmInstanceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Probes an LLM endpoint to verify connectivity.
    /// </summary>
    /// <param name="request">Probe request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Probe result</returns>
    Task<object> ProbeLlmAsync(ProbeLlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Configures embeddings provider.
    /// </summary>
    /// <param name="request">Embeddings configuration</param>
    /// <param name="ct">Cancellation token</param>
    Task UpsertEmbeddingsAsync(UpsertEmbeddingsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Lists trashed API keys (for recovery).
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of trashed API keys</returns>
    Task<IReadOnlyList<TrashedApiKeyListItem>> GetTrashedKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Restores a trashed API key.
    /// </summary>
    /// <param name="providerName">Provider name</param>
    /// <param name="ct">Cancellation token</param>
    Task RestoreTrashedKeyAsync(string providerName, CancellationToken ct = default);

    /// <summary>
    /// Sets a generic secret value.
    /// </summary>
    /// <param name="request">Secret request</param>
    /// <param name="ct">Cancellation token</param>
    Task SetSecretAsync(SetSecretRequest request, CancellationToken ct = default);

    /// <summary>
    /// Removes a secret.
    /// </summary>
    /// <param name="request">Remove secret request</param>
    /// <param name="ct">Cancellation token</param>
    Task RemoveSecretAsync(RemoveSecretRequest request, CancellationToken ct = default);
}
