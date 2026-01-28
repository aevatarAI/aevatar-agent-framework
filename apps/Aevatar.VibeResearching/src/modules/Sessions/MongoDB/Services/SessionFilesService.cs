using System.Text;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Services;

/// <summary>
/// Session files service for managing session-scoped file operations.
///
/// Responsibilities:
/// - Safe listing/reading/writing of files under workspace/sessions/{sessionId}/
/// - Path traversal protection (within session root)
/// - Guardrails:
///   - allow only small text-like files for editing
///   - bounded directory scans
/// </summary>
public sealed class SessionFilesService
{
    private const int MaxListEntries = 4000;
    private const int MaxReadBytes = 512 * 1024; // 512KB
    private const int MaxWriteBytes = 512 * 1024; // 512KB

    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".jsonl", ".yaml", ".yml", ".proto", ".cs", ".csproj", ".sln", ".ts", ".tsx", ".js", ".css", ".html"
    };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<SessionFilesService> _logger;

    public SessionFilesService(WorkspaceService workspace, ILogger<SessionFilesService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public sealed record FileNode(
        string Path,
        string Name,
        string Kind, // "dir" | "file"
        long SizeBytes,
        long UpdatedAtMs,
        List<FileNode>? Children);

    public sealed record FileReadResult(
        string Path,
        string Content,
        string Encoding,
        long SizeBytes,
        long UpdatedAtMs);

    public sealed record FileWriteResult(
        string Path,
        long SizeBytes,
        long UpdatedAtMs);

    public FileNode ListTree(string sessionId, string? relativeDir = null, int maxDepth = 6)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        maxDepth = Math.Clamp(maxDepth, 1, 12);

        var rel = NormalizeRelative(relativeDir);
        var startDir = string.IsNullOrWhiteSpace(rel) ? ws.SessionRoot : System.IO.Path.Combine(ws.SessionRoot, rel);
        startDir = System.IO.Path.GetFullPath(startDir);
        EnsureWithin(ws.SessionRoot, startDir);

        var visited = 0;
        FileNode Walk(string dirPath, string relPath, int depth)
        {
            visited++;
            if (visited > MaxListEntries) return new FileNode(relPath, System.IO.Path.GetFileName(dirPath), "dir", 0, UtcMs(), []);

            var di = new DirectoryInfo(dirPath);
            var children = new List<FileNode>();

            if (depth <= 0)
                return new FileNode(relPath, di.Name, "dir", 0, ToMs(di.LastWriteTimeUtc), children);

            // Directories first, then files; stable ordering.
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = di.EnumerateFileSystemInfos()
                    .OrderBy(x => x is DirectoryInfo ? 0 : 1)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new FileNode(relPath, di.Name, "dir", 0, ToMs(di.LastWriteTimeUtc), children);
            }

            foreach (var e in entries)
            {
                if (visited > MaxListEntries) break;
                if (e is DirectoryInfo sub)
                {
                    if (sub.Name is "bin" or "obj") continue;
                    var childRel = JoinRel(relPath, sub.Name);
                    children.Add(Walk(sub.FullName, childRel, depth - 1));
                }
                else if (e is FileInfo fi)
                {
                    var childRel = JoinRel(relPath, fi.Name);
                    children.Add(new FileNode(
                        Path: childRel,
                        Name: fi.Name,
                        Kind: "file",
                        SizeBytes: fi.Length,
                        UpdatedAtMs: ToMs(fi.LastWriteTimeUtc),
                        Children: null));
                }
            }

            return new FileNode(relPath, di.Name, "dir", 0, ToMs(di.LastWriteTimeUtc), children);
        }

        var rootRel = string.IsNullOrWhiteSpace(rel) ? "" : rel;
        return Walk(startDir, rootRel, maxDepth);
    }

    public async Task<FileReadResult> ReadTextAsync(string sessionId, string relativePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var rel = NormalizeRelative(relativePath);
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("path is required", nameof(relativePath));

        var abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(ws.SessionRoot, rel));
        EnsureWithin(ws.SessionRoot, abs);

        if (!File.Exists(abs))
            throw new FileNotFoundException("file not found", rel);

        if (!IsAllowedText(abs))
            throw new InvalidOperationException("file type is not allowed for editing");

        var fi = new FileInfo(abs);
        if (fi.Length > MaxReadBytes)
            throw new InvalidOperationException($"file too large (>{MaxReadBytes} bytes)");

        // UTF-8 (no BOM) with best-effort BOM stripping.
        var text = await File.ReadAllTextAsync(abs, Encoding.UTF8, ct);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        return new FileReadResult(
            Path: rel,
            Content: text.Replace("\r", ""),
            Encoding: "utf-8",
            SizeBytes: fi.Length,
            UpdatedAtMs: ToMs(fi.LastWriteTimeUtc));
    }

    public async Task<FileWriteResult> WriteTextAsync(string sessionId, string relativePath, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var rel = NormalizeRelative(relativePath);
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("path is required", nameof(relativePath));

        var abs = System.IO.Path.GetFullPath(System.IO.Path.Combine(ws.SessionRoot, rel));
        EnsureWithin(ws.SessionRoot, abs);

        if (!IsAllowedText(abs))
            throw new InvalidOperationException("file type is not allowed for editing");

        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > MaxWriteBytes)
            throw new InvalidOperationException($"content too large (>{MaxWriteBytes} bytes)");

        // Ensure parent dir exists.
        var parent = System.IO.Path.GetDirectoryName(abs);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        // Atomic write via ws/tmp.
        Directory.CreateDirectory(ws.TmpDir);
        var tmp = System.IO.Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content.Replace("\r", ""), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, abs, overwrite: true);

        var fi = new FileInfo(abs);
        return new FileWriteResult(rel, fi.Length, ToMs(fi.LastWriteTimeUtc));
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static long ToMs(DateTime dtUtc)
        => new DateTimeOffset(dtUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static long UtcMs()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string NormalizeRelative(string? s)
        => (s ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/').TrimEnd('/');

    private static string JoinRel(string a, string b)
    {
        a = NormalizeRelative(a);
        b = (b ?? string.Empty).Replace('\\', '/').Trim('/');
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + "/" + b;
    }

    private static void EnsureWithin(string root, string candidate)
    {
        root = System.IO.Path.GetFullPath(root);
        candidate = System.IO.Path.GetFullPath(candidate);

        if (!candidate.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(candidate, root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("path escapes session root");
        }
    }

    private static bool IsAllowedText(string absPath)
    {
        var ext = System.IO.Path.GetExtension(absPath);
        return AllowedExt.Contains(ext);
    }
}
