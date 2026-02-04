using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for SkillsMP integration and skill packs management.
/// </summary>
public interface ISkillPacksAppService : IApplicationService
{
    /// <summary>
    /// Gets the SkillsMP configuration status.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Configuration status including API key presence</returns>
    Task<object> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the SkillsMP API key (masked or revealed).
    /// </summary>
    /// <param name="reveal">Whether to reveal the full API key</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>API key information</returns>
    Task<object> GetApiKeyAsync(bool reveal = false, CancellationToken ct = default);

    /// <summary>
    /// Updates SkillsMP settings (API key and base URL).
    /// </summary>
    /// <param name="apiKey">SkillsMP API key</param>
    /// <param name="baseUrl">SkillsMP base URL</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateSettingsAsync(string? apiKey = null, string? baseUrl = null, CancellationToken ct = default);

    /// <summary>
    /// Removes SkillsMP settings.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task RemoveSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Searches skill packs on SkillsMP.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="page">Page number</param>
    /// <param name="limit">Results per page</param>
    /// <param name="sortBy">Sort field</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Search results</returns>
    Task<object> SearchAsync(string query, int page = 1, int limit = 20, string? sortBy = null, CancellationToken ct = default);

    /// <summary>
    /// Performs AI-powered search on SkillsMP.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI search results</returns>
    Task<object> AiSearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Installs a skill pack from a Git repository.
    /// </summary>
    /// <param name="repoUrl">Git repository URL</param>
    /// <param name="name">Pack name (optional, derived from repo URL if not provided)</param>
    /// <param name="reference">Git reference (branch/tag, defaults to "main")</param>
    /// <param name="skillsSubDir">Subdirectory containing skills (defaults to "skills")</param>
    /// <param name="installDir">Installation directory (optional)</param>
    /// <param name="sync">Whether to sync after installation</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Installation result</returns>
    Task<object> InstallAsync(
        string repoUrl,
        string? name = null,
        string? reference = null,
        string? skillsSubDir = null,
        string? installDir = null,
        bool sync = true,
        CancellationToken ct = default);

    /// <summary>
    /// Lists installed skill packs.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of installed skill packs</returns>
    Task<IReadOnlyList<object>> ListPacksAsync(CancellationToken ct = default);

    /// <summary>
    /// Syncs all skill packs.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Sync result</returns>
    Task<object> SyncAsync(CancellationToken ct = default);
}
