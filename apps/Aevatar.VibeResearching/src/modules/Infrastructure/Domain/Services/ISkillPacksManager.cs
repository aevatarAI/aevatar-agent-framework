namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for SkillsMP integration and skill packs management.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface ISkillPacksManager
{
    /// <summary>
    /// Gets the SkillsMP configuration status.
    /// </summary>
    Task<object> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Gets the SkillsMP API key (masked or revealed).
    /// </summary>
    Task<object> GetApiKeyAsync(bool reveal, CancellationToken ct);

    /// <summary>
    /// Updates SkillsMP settings.
    /// </summary>
    Task UpdateSettingsAsync(string? apiKey, string? baseUrl, CancellationToken ct);

    /// <summary>
    /// Removes SkillsMP settings.
    /// </summary>
    Task RemoveSettingsAsync(CancellationToken ct);

    /// <summary>
    /// Searches skill packs on SkillsMP.
    /// </summary>
    Task<object> SearchAsync(string query, int page, int limit, string? sortBy, CancellationToken ct);

    /// <summary>
    /// Performs AI-powered search on SkillsMP.
    /// </summary>
    Task<object> AiSearchAsync(string query, CancellationToken ct);

    /// <summary>
    /// Installs a skill pack from a Git repository.
    /// </summary>
    Task<object> InstallAsync(
        string repoUrl,
        string? name,
        string? reference,
        string? skillsSubDir,
        string? installDir,
        bool sync,
        CancellationToken ct);

    /// <summary>
    /// Lists installed skill packs.
    /// </summary>
    Task<IReadOnlyList<object>> ListPacksAsync(CancellationToken ct);

    /// <summary>
    /// Syncs all skill packs.
    /// </summary>
    Task<object> SyncAsync(CancellationToken ct);
}
