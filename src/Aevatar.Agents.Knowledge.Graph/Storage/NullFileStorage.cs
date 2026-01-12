namespace Aevatar.Agents.Knowledge.Graph.Storage;

/// <summary>
/// No-op file storage for when S3 is not configured.
/// Folder paths are stored as-is without uploading.
/// </summary>
public sealed class NullFileStorage : IFileStorage
{
    public Task<string> UploadFolderAsync(
        string sessionId,
        string localFolderPath,
        string? remoteFileName = null,
        CancellationToken cancellationToken = default)
    {
        // Return the local path as-is (no upload)
        return Task.FromResult(localFolderPath);
    }

    public Task DownloadFolderAsync(
        string sessionId,
        string remoteFileName,
        string localFolderPath,
        CancellationToken cancellationToken = default)
    {
        // No-op
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        // Check local folder
        return Task.FromResult(Directory.Exists(remoteFileName));
    }

    public Task DeleteAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        // No-op
        return Task.CompletedTask;
    }

    public Task<string> GetPresignedUrlAsync(
        string sessionId,
        string remoteFileName,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        // Return file:// URI
        return Task.FromResult($"file://{remoteFileName}");
    }
}
