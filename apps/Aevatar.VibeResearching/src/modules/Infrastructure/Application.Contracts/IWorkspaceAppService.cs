using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for session workspace file operations.
/// </summary>
public interface IWorkspaceAppService : IApplicationService
{
    /// <summary>
    /// Lists all files in the session workspace.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Workspace scan result</returns>
    Task<object> ListFilesAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Reads a file from the session workspace.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="path">Relative file path</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>File content</returns>
    Task<string> ReadFileAsync(string sessionId, string path, CancellationToken ct = default);

    /// <summary>
    /// Saves a file to the session workspace.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="input">File data</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveFileAsync(string sessionId, SaveFileDto input, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from the session workspace.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="path">Relative file path</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteFileAsync(string sessionId, string path, CancellationToken ct = default);

    /// <summary>
    /// Uploads files to the session workspace.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="files">Files to upload</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of uploaded file paths</returns>
    Task<IReadOnlyList<string>> UploadFilesAsync(string sessionId, IEnumerable<object> files, CancellationToken ct = default);
}
