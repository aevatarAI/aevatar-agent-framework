using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for LLM provider secrets and configuration management.
/// Delegates to LLM secrets manager.
/// </summary>
public class LlmSecretsAppService : ApplicationService, ILlmSecretsAppService
{
    private readonly ILlmSecretsManager _secretsManager;

    public LlmSecretsAppService(ILlmSecretsManager secretsManager)
    {
        _secretsManager = secretsManager;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderTypeItem>> GetProviderTypesAsync(CancellationToken ct = default)
    {
        var result = await _secretsManager.GetProviderTypesAsync(ct);
        return result.Cast<ProviderTypeItem>().ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderInstanceItem>> GetProviderInstancesAsync(CancellationToken ct = default)
    {
        var result = await _secretsManager.GetProviderInstancesAsync(ct);
        return result.Cast<ProviderInstanceItem>().ToList();
    }

    /// <inheritdoc/>
    public async Task<ResolvedProviderPublic> GetResolvedProviderAsync(string providerName, CancellationToken ct = default)
    {
        var result = await _secretsManager.GetResolvedProviderAsync(providerName, ct);
        return (ResolvedProviderPublic)result;
    }

    /// <inheritdoc/>
    public async Task SetApiKeyAsync(SetLlmApiKeyRequest request, CancellationToken ct = default)
    {
        await _secretsManager.SetApiKeyAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task SetDefaultProviderAsync(SetLlmDefaultRequest request, CancellationToken ct = default)
    {
        await _secretsManager.SetDefaultProviderAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task UpsertProviderAsync(UpsertLlmInstanceRequest request, CancellationToken ct = default)
    {
        await _secretsManager.UpsertProviderAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task<object> ProbeLlmAsync(ProbeLlmRequest request, CancellationToken ct = default)
    {
        return await _secretsManager.ProbeLlmAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task UpsertEmbeddingsAsync(UpsertEmbeddingsRequest request, CancellationToken ct = default)
    {
        await _secretsManager.UpsertEmbeddingsAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TrashedApiKeyListItem>> GetTrashedKeysAsync(CancellationToken ct = default)
    {
        var result = await _secretsManager.GetTrashedKeysAsync(ct);
        return result.Cast<TrashedApiKeyListItem>().ToList();
    }

    /// <inheritdoc/>
    public async Task RestoreTrashedKeyAsync(string providerName, CancellationToken ct = default)
    {
        await _secretsManager.RestoreTrashedKeyAsync(providerName, ct);
    }

    /// <inheritdoc/>
    public async Task SetSecretAsync(SetSecretRequest request, CancellationToken ct = default)
    {
        await _secretsManager.SetSecretAsync(request, ct);
    }

    /// <inheritdoc/>
    public async Task RemoveSecretAsync(RemoveSecretRequest request, CancellationToken ct = default)
    {
        await _secretsManager.RemoveSecretAsync(request, ct);
    }
}
