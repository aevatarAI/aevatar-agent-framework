using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aevatar.Novel.Sidecar.Services.DeviationImpact;

// ============================================================
//  ChapterRevisionStore (v1)
//
//  PURPOSE:
//  - Track a chapter's incremental revisions triggered by SSOT saves.
//  - Persist snapshots under storyRoot/.index/revisions/<chapterId>/rev_<n>.txt
//  - Maintain meta.json with current revision + hash.
//
//  NOTE:
//  - This is derived data (file SSOT still wins); .index is ignored by watcher.
// ============================================================

public sealed class ChapterRevisionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<RevisionChange?> TryRegisterRevisionAsync(
        string storyRoot,
        string chapterId,
        string chapterFullPath,
        CancellationToken ct)
    {
        var dir = GetChapterRevisionDir(storyRoot, chapterId);
        Directory.CreateDirectory(dir);

        var metaPath = Path.Combine(dir, "meta.json");
        var currentText = await ReadAllTextSharedAsync(chapterFullPath, ct);
        var currentHash = "sha256:" + ComputeSha256Hex(Encoding.UTF8.GetBytes(currentText));

        var meta = await ReadMetaAsync(metaPath, ct);
        if (meta is null)
        {
            // First observation => baseline revision 0
            await WriteSnapshotAsync(dir, 0, currentText, ct);
            await WriteMetaAsync(metaPath, new RevisionMeta(0, currentHash, DateTime.UtcNow), ct);
            return null;
        }

        if (string.Equals(meta.CurrentHash, currentHash, StringComparison.OrdinalIgnoreCase))
            return null;

        var baseRev = meta.CurrentRevision;
        var editedRev = baseRev + 1;

        var baseText = await ReadSnapshotAsync(dir, baseRev, ct) ?? "";
        await WriteSnapshotAsync(dir, editedRev, currentText, ct);
        await WriteMetaAsync(metaPath, new RevisionMeta(editedRev, currentHash, DateTime.UtcNow), ct);

        return new RevisionChange(baseRev, editedRev, baseText, currentText, currentHash);
    }

    private static string GetChapterRevisionDir(string storyRoot, string chapterId)
        => Path.Combine(storyRoot, ".index", "revisions", chapterId);

    private static string SnapshotPath(string dir, long revision)
        => Path.Combine(dir, $"rev_{revision:000000}.txt");

    private static async Task<string?> ReadSnapshotAsync(string dir, long revision, CancellationToken ct)
    {
        var path = SnapshotPath(dir, revision);
        if (!File.Exists(path))
            return null;
        return await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
    }

    private static async Task WriteSnapshotAsync(string dir, long revision, string content, CancellationToken ct)
    {
        var path = SnapshotPath(dir, revision);
        await AtomicWriteAsync(path, content, ct);
    }

    private static async Task<RevisionMeta?> ReadMetaAsync(string metaPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(metaPath))
                return null;
            var json = await File.ReadAllTextAsync(metaPath, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return JsonSerializer.Deserialize<RevisionMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Task WriteMetaAsync(string metaPath, RevisionMeta meta, CancellationToken ct)
        => AtomicWriteAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), ct);

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ReadAllTextSharedAsync(string fullPath, CancellationToken ct)
    {
        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return await sr.ReadToEndAsync(ct);
    }

    private sealed record RevisionMeta(
        long CurrentRevision,
        string CurrentHash,
        DateTime UpdatedAtUtc);
}

public sealed record RevisionChange(
    long BaseRevision,
    long EditedRevision,
    string BaseText,
    string EditedText,
    string EditedHash);


