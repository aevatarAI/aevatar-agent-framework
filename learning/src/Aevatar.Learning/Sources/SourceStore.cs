using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Learning.Notebooks;

namespace Aevatar.Learning.Sources;

// ============================================================
//  SourceStore (MVP)
//
//  目标：
//  - 把导入的资料（txt/md）持久化到 NotebookWorkspace.SourcesDir
//  - 生成稳定 sourceId（内容 hash 前缀），并提供 list/get
//
//  安全约束：
//  - 只处理文本（UTF-8），不执行任何二进制
//  - 有界：大小上限，避免一次性读入超大内容
// ============================================================
public sealed class SourceStore
{
    // 以“字符数”作为最粗粒度上限（MVP），避免超大文本把内存打爆。
    public const int MaxSourceChars = 1_000_000; // ~1MB 级别（按 1 char ~= 1 byte 粗估）

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SourceMeta> CreateTextAsync(
        NotebookWorkspace workspace,
        string title,
        string content,
        string? mimeType = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();

        title = (title ?? string.Empty).Trim();
        content ??= string.Empty;
        mimeType = NormalizeMimeType(mimeType);

        if (title.Length == 0)
            throw new ArgumentException("title is required.", nameof(title));
        if (content.Length == 0)
            throw new ArgumentException("content is required.", nameof(content));
        if (content.Length > MaxSourceChars)
            throw new InvalidOperationException($"content too large: {content.Length} chars (max {MaxSourceChars}).");

        // Stable id: sha256(content) prefix.
        var sha = Sha256Hex(content);
        var sourceId = sha[..12];

        var dir = Path.Combine(workspace.SourcesDir, sourceId);
        Directory.CreateDirectory(dir);

        var ext = mimeType == "text/markdown" ? "md" : "txt";
        var contentPath = Path.Combine(dir, $"content.{ext}");
        var metaPath = Path.Combine(dir, "meta.json");

        var now = DateTimeOffset.UtcNow;
        var meta = new SourceMeta
        {
            SourceId = sourceId,
            Title = title,
            MimeType = mimeType,
            SizeChars = content.Length,
            Sha256Hex = sha,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Best-effort idempotency: if meta exists and sha matches, treat as already imported.
        if (File.Exists(metaPath))
        {
            var existing = await TryReadMetaAsync(metaPath, ct);
            if (existing != null && string.Equals(existing.Sha256Hex, sha, StringComparison.OrdinalIgnoreCase))
                return existing;
        }

        await File.WriteAllTextAsync(contentPath, content, new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, Json), new UTF8Encoding(false), ct);

        return meta;
    }

    public async Task<IReadOnlyList<SourceMeta>> ListAsync(NotebookWorkspace workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!Directory.Exists(workspace.SourcesDir))
            return Array.Empty<SourceMeta>();

        var list = new List<SourceMeta>(capacity: 32);
        foreach (var dir in Directory.EnumerateDirectories(workspace.SourcesDir))
        {
            ct.ThrowIfCancellationRequested();

            var metaPath = Path.Combine(dir, "meta.json");
            var meta = await TryReadMetaAsync(metaPath, ct);
            if (meta != null)
                list.Add(meta);
        }

        return list
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<(SourceMeta Meta, string Content)> GetAsync(
        NotebookWorkspace workspace,
        string sourceId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        sourceId = (sourceId ?? string.Empty).Trim();
        if (sourceId.Length == 0)
            throw new ArgumentException("sourceId is required.", nameof(sourceId));

        var dir = Path.Combine(workspace.SourcesDir, sourceId);
        var metaPath = Path.Combine(dir, "meta.json");
        var meta = await TryReadMetaAsync(metaPath, ct);
        if (meta == null)
            throw new FileNotFoundException("source not found", metaPath);

        var contentPath = ResolveContentPath(dir, meta.MimeType);
        if (!File.Exists(contentPath))
            throw new FileNotFoundException("source content not found", contentPath);

        // Guard: avoid unbounded reads if file was modified externally.
        var text = await ReadAllTextWithLimitAsync(contentPath, MaxSourceChars, ct);
        return (meta, text);
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static string NormalizeMimeType(string? mimeType)
    {
        mimeType = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (mimeType.Length == 0) return "text/plain";

        return mimeType switch
        {
            "text/plain" => "text/plain",
            "text/markdown" => "text/markdown",
            "text/md" => "text/markdown",
            _ => throw new InvalidOperationException($"unsupported mimeType: {mimeType}")
        };
    }

    private static string ResolveContentPath(string dir, string mimeType)
    {
        var ext = mimeType == "text/markdown" ? "md" : "txt";
        return Path.Combine(dir, $"content.{ext}");
    }

    private static string Sha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<SourceMeta?> TryReadMetaAsync(string metaPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(metaPath))
                return null;

            var json = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<SourceMeta>(json, Json);
            if (meta == null || string.IsNullOrWhiteSpace(meta.SourceId))
                return null;

            meta.SourceId = meta.SourceId.Trim();
            meta.Title = (meta.Title ?? string.Empty).Trim();
            meta.MimeType = NormalizeMimeType(meta.MimeType);
            meta.Sha256Hex = (meta.Sha256Hex ?? string.Empty).Trim().ToLowerInvariant();
            return meta;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadAllTextWithLimitAsync(string filePath, int maxChars, CancellationToken ct)
    {
        // Use streaming reader to avoid unbounded memory spikes.
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0) break;

            var remaining = maxChars - sb.Length;
            if (remaining <= 0)
                throw new InvalidOperationException($"source too large (>{maxChars} chars)");

            sb.Append(buffer, 0, Math.Min(read, remaining));
        }

        return sb.ToString();
    }
}

// NOTE:
// - This is an internal storage/meta model (HTTP DTO can reuse it).
// - If/when the meta needs to cross runtime boundaries, move to Protobuf.
public sealed class SourceMeta
{
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string MimeType { get; set; } = "text/plain";
    public int SizeChars { get; set; }
    public string Sha256Hex { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}


