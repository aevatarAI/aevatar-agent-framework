using Aevatar.VibeResearching.Infrastructure;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Secrets;

/// <summary>
/// Default stub implementation of ILlmSecretsManager.
/// Returns not-configured responses until LLM provider secrets are set up.
/// </summary>
public sealed class DefaultLlmSecretsManager : ILlmSecretsManager
{
    private static readonly object NotConfigured = new { configured = false, message = "LLM secrets not configured." };

    /// <inheritdoc />
    public Task<object> GetStatusAsync(CancellationToken ct) => Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task<object> GetProviderCatalogAsync(CancellationToken ct) =>
        Task.FromResult<object>(new { providers = Array.Empty<object>() });

    /// <inheritdoc />
    public Task<object> GetProviderProfilesAsync(CancellationToken ct) =>
        Task.FromResult<object>(new { profiles = Array.Empty<object>() });

    /// <inheritdoc />
    public Task UpdateProviderAsync(string providerName, object configuration, CancellationToken ct) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task RemoveProviderAsync(string providerName, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<object> ResolveProviderAsync(string modelOrRequest, CancellationToken ct) =>
        Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task<IReadOnlyList<object>> GetProviderTypesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

    /// <inheritdoc />
    public Task<IReadOnlyList<object>> GetProviderInstancesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

    /// <inheritdoc />
    public Task<object> GetResolvedProviderAsync(string providerName, CancellationToken ct) =>
        Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task SetApiKeyAsync(object request, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetDefaultProviderAsync(object request, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task UpsertProviderAsync(object request, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<object> ProbeLlmAsync(object request, CancellationToken ct) =>
        Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task UpsertEmbeddingsAsync(object request, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<object>> GetTrashedKeysAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

    /// <inheritdoc />
    public Task RestoreTrashedKeyAsync(string providerName, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SetSecretAsync(object request, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RemoveSecretAsync(object request, CancellationToken ct) => Task.CompletedTask;
}
