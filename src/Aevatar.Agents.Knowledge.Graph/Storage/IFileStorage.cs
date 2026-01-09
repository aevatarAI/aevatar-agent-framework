namespace Aevatar.Agents.Knowledge.Graph.Storage;

/// <summary>
/// Abstraction for file storage (S3, local filesystem, etc.).
/// Files are organized by session: {rootBucket}/{sessionId}/{filename}
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Uploads a local folder to storage as a zip archive.
    /// The folder is compressed and uploaded as {folderName}.zip.
    /// </summary>
    /// <param name="sessionId">Session ID for bucket isolation.</param>
    /// <param name="localFolderPath">Path to the local folder to upload.</param>
    /// <param name="remoteFileName">Optional remote filename (without .zip extension). If null, uses folder name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The storage URI (e.g., s3://bucket/session/folder.zip).</returns>
    Task<string> UploadFolderAsync(
        string sessionId,
        string localFolderPath,
        string? remoteFileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a zip archive from storage and extracts to a local folder.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="remoteFileName">Remote filename (the .zip file).</param>
    /// <param name="localFolderPath">Local folder path to extract to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DownloadFolderAsync(
        string sessionId,
        string remoteFileName,
        string localFolderPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists in storage.
    /// </summary>
    Task<bool> ExistsAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    Task DeleteAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a presigned URL for direct download (valid for limited time).
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="remoteFileName">Remote filename.</param>
    /// <param name="expiresIn">Expiration time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Presigned URL.</returns>
    Task<string> GetPresignedUrlAsync(
        string sessionId,
        string remoteFileName,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}
