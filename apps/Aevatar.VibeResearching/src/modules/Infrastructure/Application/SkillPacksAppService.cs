using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for SkillsMP integration and skill packs management.
/// Delegates to skill packs manager.
/// </summary>
public class SkillPacksAppService : ApplicationService, ISkillPacksAppService
{
    private readonly ISkillPacksManager _skillPacksManager;

    public SkillPacksAppService(ISkillPacksManager skillPacksManager)
    {
        _skillPacksManager = skillPacksManager;
    }

    /// <inheritdoc/>
    public async Task<object> GetStatusAsync(CancellationToken ct = default)
    {
        return await _skillPacksManager.GetStatusAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetApiKeyAsync(bool reveal = false, CancellationToken ct = default)
    {
        return await _skillPacksManager.GetApiKeyAsync(reveal, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateSettingsAsync(string? apiKey = null, string? baseUrl = null, CancellationToken ct = default)
    {
        await _skillPacksManager.UpdateSettingsAsync(apiKey, baseUrl, ct);
    }

    /// <inheritdoc/>
    public async Task RemoveSettingsAsync(CancellationToken ct = default)
    {
        await _skillPacksManager.RemoveSettingsAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> SearchAsync(
        string query,
        int page = 1,
        int limit = 20,
        string? sortBy = null,
        CancellationToken ct = default)
    {
        return await _skillPacksManager.SearchAsync(query, page, limit, sortBy, ct);
    }

    /// <inheritdoc/>
    public async Task<object> AiSearchAsync(string query, CancellationToken ct = default)
    {
        return await _skillPacksManager.AiSearchAsync(query, ct);
    }

    /// <inheritdoc/>
    public async Task<object> InstallAsync(
        string repoUrl,
        string? name = null,
        string? reference = null,
        string? skillsSubDir = null,
        string? installDir = null,
        bool sync = true,
        CancellationToken ct = default)
    {
        return await _skillPacksManager.InstallAsync(repoUrl, name, reference, skillsSubDir, installDir, sync, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<object>> ListPacksAsync(CancellationToken ct = default)
    {
        return await _skillPacksManager.ListPacksAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> SyncAsync(CancellationToken ct = default)
    {
        return await _skillPacksManager.SyncAsync(ct);
    }
}
