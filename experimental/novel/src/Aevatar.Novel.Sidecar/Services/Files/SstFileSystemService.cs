using System.Security.Cryptography;
using System.Text;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.Files;

// ============================================================
//  SstFileSystemService
//
//  PURPOSE:
//  - Provide safe file operations for UI (Tauri/React) via sidecar.
//  - Enforce SSOT rules:
//    - Only operate under ProjectRoot
//    - Only allow .txt/.md (v1)
//    - Atomic writes
//    - Optional optimistic concurrency via sha256
//
//  NOTE:
//  - UI could read/write files directly, but sidecar APIs simplify cross-platform
//    behavior and keep a single guardrail layer.
// ============================================================

public sealed class SstFileSystemService
{
    private const int DefaultMaxInlineBytes = 1_000_000; // 1MB

    private readonly ProjectRootManager _projectRoot;
    private readonly ILogger<SstFileSystemService> _logger;

    public SstFileSystemService(ProjectRootManager projectRoot, ILogger<SstFileSystemService> logger)
    {
        _projectRoot = projectRoot;
        _logger = logger;
    }

    public ListDirectoryResponse ListDirectory(ListDirectoryRequest req)
    {
        var rel = (req.RelativePath ?? string.Empty).Trim();
        var root = _projectRoot.GetProjectRoot();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new ListDirectoryResponse
            {
                RelativePath = rel
            };
        }

        if (!TryResolvePath(root, rel, isDirectory: true, out var fullPath, out _))
        {
            return new ListDirectoryResponse { RelativePath = rel };
        }

        if (!Directory.Exists(fullPath))
        {
            return new ListDirectoryResponse { RelativePath = rel };
        }

        var includeDirs = req.IncludeDirectories || (!req.IncludeDirectories && !req.IncludeFiles); // default: both
        var includeFiles = req.IncludeFiles || (!req.IncludeDirectories && !req.IncludeFiles);
        var onlySupported = req.OnlySupportedTextFiles;

        var entries = new List<FileEntry>();

        if (includeDirs)
        {
            foreach (var dir in Directory.EnumerateDirectories(fullPath, "*", SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(dir);
                var entryRel = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                entries.Add(new FileEntry
                {
                    RelativePath = entryRel,
                    IsDirectory = true,
                    SizeBytes = 0,
                    LastWriteTime = Timestamp.FromDateTime(info.LastWriteTimeUtc)
                });
            }
        }

        if (includeFiles)
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (onlySupported && !IsSupportedTextFile(info.FullName))
                    continue;

                var entryRel = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                entries.Add(new FileEntry
                {
                    RelativePath = entryRel,
                    IsDirectory = false,
                    SizeBytes = info.Length,
                    LastWriteTime = Timestamp.FromDateTime(info.LastWriteTimeUtc)
                });
            }
        }

        // Stable ordering for UI.
        entries = entries
            .OrderBy(e => e.IsDirectory ? 0 : 1)
            .ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resp = new ListDirectoryResponse { RelativePath = rel };
        resp.Entries.AddRange(entries);
        return resp;
    }

    public async Task<ReadTextFileResponse> ReadTextFileAsync(ReadTextFileRequest req, CancellationToken ct)
    {
        var rel = (req.RelativePath ?? string.Empty).Trim();
        var root = _projectRoot.GetProjectRoot();

        var resp = new ReadTextFileResponse { RelativePath = rel };

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            resp.Exists = false;
            return resp;
        }

        if (!TryResolvePath(root, rel, isDirectory: false, out var fullPath, out var error))
        {
            resp.Exists = false;
            resp.Labels["error"] = error;
            return resp;
        }

        if (!File.Exists(fullPath))
        {
            resp.Exists = false;
            return resp;
        }

        if (!IsSupportedTextFile(fullPath))
        {
            resp.Exists = false;
            resp.Labels["error"] = "unsupported_file_type";
            return resp;
        }

        var info = new FileInfo(fullPath);
        resp.Exists = true;
        resp.SizeBytes = info.Length;
        resp.LastWriteTime = Timestamp.FromDateTime(info.LastWriteTimeUtc);

        var maxInlineBytes = req.MaxInlineBytes > 0 ? req.MaxInlineBytes : DefaultMaxInlineBytes;
        var format = GetTextFormat(fullPath);

        if (info.Length <= maxInlineBytes)
        {
            var text = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
            resp.Content = new TextPayload
            {
                Format = format,
                InlineText = text,
                Preview = BuildPreview(text)
            };
            resp.ContentHash = "sha256:" + ComputeSha256Hex(Encoding.UTF8.GetBytes(text));
            return resp;
        }

        // Large file: return blob_ref + preview (best-effort preview from first N bytes).
        var preview = await ReadPreviewAsync(fullPath, maxBytes: Math.Min(16_000, maxInlineBytes), ct);
        resp.Content = new TextPayload
        {
            Format = format,
            BlobRef = new TextBlobRef
            {
                Uri = $"file://{fullPath.Replace('\\', '/')}",
                SizeBytes = info.Length
            },
            Preview = preview
        };
        resp.ContentHash = ""; // intentionally omitted for large files in v1
        return resp;
    }

    public async Task<WriteTextFileResponse> WriteTextFileAsync(WriteTextFileRequest req, CancellationToken ct)
    {
        var rel = (req.RelativePath ?? string.Empty).Trim();
        var root = _projectRoot.GetProjectRoot();

        var resp = new WriteTextFileResponse { RelativePath = rel };

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            resp.Success = false;
            resp.Message = "project_root_not_set";
            return resp;
        }

        if (!TryResolvePath(root, rel, isDirectory: false, out var fullPath, out var error))
        {
            resp.Success = false;
            resp.Message = error;
            return resp;
        }

        if (!IsSupportedTextFile(fullPath))
        {
            resp.Success = false;
            resp.Message = "unsupported_file_type";
            return resp;
        }

        if (req.Content is null || req.Content.ValueCase != TextPayload.ValueOneofCase.InlineText)
        {
            resp.Success = false;
            resp.Message = "content_inline_text_required";
            return resp;
        }

        var expected = (req.ExpectedContentHash ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(expected) && File.Exists(fullPath))
        {
            var currentHash = await ComputeSha256HexAsync(fullPath, ct);
            var current = "sha256:" + currentHash;
            if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            {
                resp.Success = false;
                resp.Message = "conflict";
                resp.ContentHash = current;
                return resp;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var content = req.Content.InlineText ?? string.Empty;
        await AtomicWriteAsync(fullPath, content, ct);

        var info = new FileInfo(fullPath);
        resp.Success = true;
        resp.Message = "ok";
        resp.SizeBytes = info.Length;
        resp.LastWriteTime = Timestamp.FromDateTime(info.LastWriteTimeUtc);
        resp.ContentHash = "sha256:" + await ComputeSha256HexAsync(fullPath, ct);
        return resp;
    }

    private static TextFormat GetTextFormat(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return TextFormat.Markdown;
        return TextFormat.PlainText;
    }

    private static bool IsSupportedTextFile(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var normalized = text.Replace("\r\n", "\n");
        var max = Math.Min(400, normalized.Length);
        return normalized[..max];
    }

    private static async Task<string> ReadPreviewAsync(string fullPath, int maxBytes, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(maxBytes, (int)Math.Min(int.MaxValue, fs.Length))];
            var read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0) return "";
            return Encoding.UTF8.GetString(buffer, 0, read).Replace("\r\n", "\n");
        }
        catch
        {
            return "";
        }
    }

    private static async Task AtomicWriteAsync(string fullPath, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);
        File.Move(tmp, fullPath, overwrite: true);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256HexAsync(string fullPath, CancellationToken ct)
    {
        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryResolvePath(string projectRoot, string relativePath, bool isDirectory, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";

        try
        {
            var root = Path.GetFullPath(projectRoot.Trim());
            var rel = (relativePath ?? string.Empty).Trim().Replace('\\', '/');

            // Normalize: empty means root itself (for directory list).
            if (string.IsNullOrWhiteSpace(rel))
            {
                fullPath = root;
                return true;
            }

            // Prevent absolute paths from sneaking in.
            if (Path.IsPathRooted(rel))
            {
                error = "relative_path_required";
                return false;
            }

            var combined = Path.GetFullPath(Path.Combine(root, rel));
            if (!IsUnderRoot(root, combined))
            {
                error = "path_outside_project_root";
                return false;
            }

            fullPath = combined;
            return true;
        }
        catch (Exception ex)
        {
            error = "path_resolution_failed:" + ex.Message;
            return false;
        }
    }

    private static bool IsUnderRoot(string rootFullPath, string candidateFullPath)
    {
        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return candidateFullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase);
    }
}


