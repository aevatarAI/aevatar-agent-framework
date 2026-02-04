namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for session files management.
/// Provides safe listing/reading/writing of files under workspace/sessions/{sessionId}/.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface ISessionFilesService
{
    /// <summary>
    /// File node representing a file or directory in the session workspace.
    /// </summary>
    /// <param name="Path">Relative path from session root.</param>
    /// <param name="Name">File or directory name.</param>
    /// <param name="Kind">"dir" or "file".</param>
    /// <param name="SizeBytes">File size in bytes.</param>
    /// <param name="UpdatedAtMs">Last modified timestamp in milliseconds.</param>
    /// <param name="Children">Child nodes for directories.</param>
    public sealed record FileNode(
        string Path,
        string Name,
        string Kind,
        long SizeBytes,
        long UpdatedAtMs,
        List<FileNode>? Children);

    /// <summary>
    /// Result of a file read operation.
    /// </summary>
    /// <param name="Path">Relative path from session root.</param>
    /// <param name="Content">File content as UTF-8 text.</param>
    /// <param name="Encoding">Encoding used (always "utf-8").</param>
    /// <param name="SizeBytes">File size in bytes.</param>
    /// <param name="UpdatedAtMs">Last modified timestamp in milliseconds.</param>
    public sealed record FileReadResult(
        string Path,
        string Content,
        string Encoding,
        long SizeBytes,
        long UpdatedAtMs);

    /// <summary>
    /// Result of a file write operation.
    /// </summary>
    /// <param name="Path">Relative path from session root.</param>
    /// <param name="SizeBytes">Written file size in bytes.</param>
    /// <param name="UpdatedAtMs">Last modified timestamp in milliseconds.</param>
    public sealed record FileWriteResult(
        string Path,
        long SizeBytes,
        long UpdatedAtMs);

    /// <summary>
    /// Lists files and directories in the session workspace as a tree.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="relativeDir">Relative directory to start from (null for root).</param>
    /// <param name="maxDepth">Maximum tree depth (1-12).</param>
    /// <returns>Root file node with children.</returns>
    FileNode ListTree(string sessionId, string? relativeDir, int maxDepth);

    /// <summary>
    /// Reads a text file from the session workspace.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="relativePath">Relative path from session root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>File read result.</returns>
    Task<FileReadResult> ReadTextAsync(string sessionId, string relativePath, CancellationToken ct);

    /// <summary>
    /// Writes a text file to the session workspace.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="relativePath">Relative path from session root.</param>
    /// <param name="content">File content as UTF-8 text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>File write result.</returns>
    Task<FileWriteResult> WriteTextAsync(string sessionId, string relativePath, string content, CancellationToken ct);

    /// <summary>
    /// Reads a file from the session workspace (returns content as string).
    /// </summary>
    Task<string> ReadFileAsync(string sessionId, string relativePath, CancellationToken ct);

    /// <summary>
    /// Saves a file to the session workspace.
    /// </summary>
    Task SaveFileAsync(string sessionId, string relativePath, string content, CancellationToken ct);

    /// <summary>
    /// Deletes a file from the session workspace.
    /// </summary>
    Task DeleteFileAsync(string sessionId, string relativePath, CancellationToken ct);
}
