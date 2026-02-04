using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for session workspace file operations.
/// Delegates to workspace and session files services.
/// </summary>
public class WorkspaceAppService : ApplicationService, IWorkspaceAppService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ISessionFilesService _sessionFilesService;

    public WorkspaceAppService(
        IWorkspaceService workspaceService,
        ISessionFilesService sessionFilesService)
    {
        _workspaceService = workspaceService;
        _sessionFilesService = sessionFilesService;
    }

    /// <inheritdoc/>
    public Task<object> ListFilesAsync(string sessionId, CancellationToken ct = default)
    {
        var result = _workspaceService.ScanWorkspace(sessionId);
        return Task.FromResult<object>(result);
    }

    /// <inheritdoc/>
    public async Task<string> ReadFileAsync(string sessionId, string path, CancellationToken ct = default)
    {
        return await _sessionFilesService.ReadFileAsync(sessionId, path, ct);
    }

    /// <inheritdoc/>
    public async Task SaveFileAsync(string sessionId, SaveFileDto input, CancellationToken ct = default)
    {
        await _sessionFilesService.SaveFileAsync(
            sessionId,
            input.Path ?? string.Empty,
            input.Content ?? string.Empty,
            ct);
    }

    /// <inheritdoc/>
    public async Task DeleteFileAsync(string sessionId, string path, CancellationToken ct = default)
    {
        await _sessionFilesService.DeleteFileAsync(sessionId, path, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> UploadFilesAsync(
        string sessionId,
        IEnumerable<object> files,
        CancellationToken ct = default)
    {
        // TODO: This needs proper implementation with file upload handling
        // For now, return empty list as placeholder
        return Array.Empty<string>();
    }
}
