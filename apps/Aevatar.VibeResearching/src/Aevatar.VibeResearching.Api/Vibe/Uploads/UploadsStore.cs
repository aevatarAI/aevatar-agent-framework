using System.Security.Cryptography;
using System.Text;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Vibe.Uploads;

// ============================================================
//  UploadsStore (Session artifacts)
//
//  - Stores user-provided files under:
//      workspace/sessions/{sessionId}/artifacts/uploads/
//  - Returns normalized relative paths for attachment references.
//  - Security: allowlist extensions + size cap + within-root.
// ============================================================

public sealed class UploadsStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".pdf"
    };

    private const long MaxFileBytes = 15 * 1024 * 1024; // 15MB per file (MVP)
    private const int MaxFilesPerRequest = 12;

    private readonly WorkspaceService _workspace;
    private readonly ILogger<UploadsStore> _logger;

    public UploadsStore(WorkspaceService workspace, ILogger<UploadsStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SaveAsync(
        string sessionId,
        IFormFile file,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var uploadsDir = Path.Combine(ws.ArtifactsDir, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var name = (file.FileName ?? string.Empty).Trim();
        var ext = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            throw new ArgumentException($"unsupported file extension: '{ext}'", nameof(file));

        if (file.Length <= 0)
            throw new ArgumentException("empty file", nameof(file));

        if (file.Length > MaxFileBytes)
            throw new ArgumentException($"file too large (max {MaxFileBytes} bytes)", nameof(file));

        // Derive stable-ish name: {yyyyMMdd_HHmmss}_{sha256_8}{ext}
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");

        await using var input = file.OpenReadStream();
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(input, ct);
        var shortHash = Convert.ToHexString(hash).ToLowerInvariant()[..8];

        var targetName = $"{stamp}_{shortHash}{ext.ToLowerInvariant()}";

        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        var target = Path.Combine(uploadsDir, targetName);

        // Copy to tmp first, then move into uploads dir (best-effort atomicity).
        await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs, ct);
        }

        File.Move(tmp, target, overwrite: true);

        // Return relative path under session root, normalized to '/'.
        var rel = Path.GetRelativePath(ws.SessionRoot, target).Replace('\\', '/').Trim('/');
        return rel;
    }

    public int GetMaxFilesPerRequest() => MaxFilesPerRequest;
}


