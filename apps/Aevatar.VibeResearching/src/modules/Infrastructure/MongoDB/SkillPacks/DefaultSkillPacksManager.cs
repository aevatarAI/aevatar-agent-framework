using Aevatar.VibeResearching.Infrastructure;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.SkillPacks;

/// <summary>
/// Default stub implementation of ISkillPacksManager.
/// Returns not-configured responses until SkillsMP integration is set up.
/// </summary>
public sealed class DefaultSkillPacksManager : ISkillPacksManager
{
    private static readonly object NotConfigured = new { configured = false, message = "SkillsMP integration not configured." };

    /// <inheritdoc />
    public Task<object> GetStatusAsync(CancellationToken ct) => Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task<object> GetApiKeyAsync(bool reveal, CancellationToken ct) => Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task UpdateSettingsAsync(string? apiKey, string? baseUrl, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RemoveSettingsAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<object> SearchAsync(string query, int page, int limit, string? sortBy, CancellationToken ct) =>
        Task.FromResult<object>(new { results = Array.Empty<object>() });

    /// <inheritdoc />
    public Task<object> AiSearchAsync(string query, CancellationToken ct) =>
        Task.FromResult<object>(new { results = Array.Empty<object>() });

    /// <inheritdoc />
    public Task<object> InstallAsync(
        string repoUrl,
        string? name,
        string? reference,
        string? skillsSubDir,
        string? installDir,
        bool sync,
        CancellationToken ct) => Task.FromResult(NotConfigured);

    /// <inheritdoc />
    public Task<IReadOnlyList<object>> ListPacksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());

    /// <inheritdoc />
    public Task<object> SyncAsync(CancellationToken ct) => Task.FromResult(NotConfigured);
}
