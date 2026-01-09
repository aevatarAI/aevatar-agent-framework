using System.IO.Compression;
using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Knowledge.Graph.Storage;

/// <summary>
/// S3-compatible file storage using Minio client.
/// Folders are zipped and stored as: {rootBucket}/{sessionId}/{folderName}.zip
/// </summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly IMinioClient _client;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;

    public S3FileStorage(
        IMinioClient client,
        IOptions<S3StorageOptions> options,
        ILogger<S3FileStorage> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadFolderAsync(
        string sessionId,
        string localFolderPath,
        string? remoteFileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

        if (!Directory.Exists(localFolderPath))
        {
            throw new DirectoryNotFoundException($"Local folder not found: {localFolderPath}");
        }

        // Determine the zip filename
        var folderName = Path.GetFileName(localFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var zipFileName = (remoteFileName ?? folderName) + ".zip";
        var objectName = $"{sessionId}/{zipFileName}";

        // Create temp zip file
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            // Zip the folder
            _logger.LogDebug("Compressing folder {FolderPath} to {ZipPath}", localFolderPath, tempZipPath);
            ZipFile.CreateFromDirectory(localFolderPath, tempZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            await EnsureBucketExistsAsync(cancellationToken);

            // Upload the zip file
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectName)
                .WithFileName(tempZipPath)
                .WithContentType("application/zip"),
                cancellationToken);

            // Generate presigned HTTPS URL (valid for 7 days - max for S3)
            var presignedUrl = await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectName)
                .WithExpiry((int)TimeSpan.FromDays(7).TotalSeconds));

            _logger.LogDebug("Uploaded folder {FolderPath}, presigned URL: {Url}", localFolderPath, presignedUrl);

            return presignedUrl;
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    public async Task DownloadFolderAsync(
        string sessionId,
        string remoteFileName,
        string localFolderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

        var objectName = $"{sessionId}/{remoteFileName}";

        // Create temp file for download
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            // Download the zip file
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectName)
                .WithFile(tempZipPath),
                cancellationToken);

            // Ensure target directory exists
            if (!Directory.Exists(localFolderPath))
            {
                Directory.CreateDirectory(localFolderPath);
            }

            // Extract the zip file
            _logger.LogDebug("Extracting {ZipPath} to {FolderPath}", tempZipPath, localFolderPath);
            ZipFile.ExtractToDirectory(tempZipPath, localFolderPath, overwriteFiles: true);

            _logger.LogDebug("Downloaded and extracted {ObjectName} to {FolderPath}", objectName, localFolderPath);
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    public async Task<bool> ExistsAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName);

        var objectName = $"{sessionId}/{remoteFileName}";

        try
        {
            await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectName),
                cancellationToken);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task DeleteAsync(
        string sessionId,
        string remoteFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName);

        var objectName = $"{sessionId}/{remoteFileName}";

        await _client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName),
            cancellationToken);

        _logger.LogDebug("Deleted {ObjectName}", objectName);
    }

    public async Task<string> GetPresignedUrlAsync(
        string sessionId,
        string remoteFileName,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName);

        var objectName = $"{sessionId}/{remoteFileName}";

        var url = await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName)
            .WithExpiry((int)expiresIn.TotalSeconds));

        return url;
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_options.BucketName),
            cancellationToken);

        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_options.BucketName),
                cancellationToken);
            _logger.LogInformation("Created bucket: {BucketName}", _options.BucketName);
        }
    }
}

/// <summary>
/// Options for S3 storage configuration.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>
    /// S3 endpoint URL (e.g., "localhost:9000" for Minio, "s3.amazonaws.com" for AWS).
    /// For AWS, you can use the regional endpoint like "s3.us-east-1.amazonaws.com".
    /// </summary>
    public string Endpoint { get; set; } = "localhost:9000";

    /// <summary>
    /// AWS region (e.g., "us-east-1", "ap-southeast-1"). Required for AWS S3.
    /// Leave null or empty for non-AWS S3-compatible services like MinIO.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Access key / username.
    /// </summary>
    public string AccessKey { get; set; } = "";

    /// <summary>
    /// Secret key / password.
    /// </summary>
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// Root bucket name for knowledge graph files.
    /// </summary>
    public string BucketName { get; set; } = "knowledge-graph";

    /// <summary>
    /// Use SSL/TLS. Should be true for AWS S3.
    /// </summary>
    public bool UseSsl { get; set; } = false;
}
