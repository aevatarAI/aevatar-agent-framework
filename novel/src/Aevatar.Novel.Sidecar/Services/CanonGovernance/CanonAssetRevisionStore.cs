using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aevatar.Novel.Sidecar.Services.CanonGovernance;

// ============================================================
//  CanonAssetRevisionStore (v1)
//
//  - Track revisions for canon assets (markdown) under a story root.
//  - Derived storage under storyRoot/.index/canon_revisions/<assetKey>/rev_<n>.md
//  - meta.json keeps current revision + sha256
// ============================================================

public sealed class CanonAssetRevisionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<CanonRevisionChange?> TryRegisterRevisionAsync(
        string storyRoot,
        string assetKey,
        string fullPath,
        CancellationToken ct)
    {
        var dir = Path.Combine(storyRoot, ".index", "canon_revisions", assetKey);
        Directory.CreateDirectory(dir);

        var metaPath = Path.Combine(dir, "meta.json");
        var currentText = await ReadAllTextSharedAsync(fullPath, ct);
        var currentHash = "sha256:" + ComputeSha256Hex(Encoding.UTF8.GetBytes(currentText));

        var meta = await ReadMetaAsync(metaPath, ct);
        if (meta is null)
        {
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

        return new CanonRevisionChange(baseRev, editedRev, baseText, currentText, currentHash);
    }

    private static string SnapshotPath(string dir, long revision)
        => Path.Combine(dir, $"rev_{revision:000000}.md");

    private static async Task<string?> ReadSnapshotAsync(string dir, long revision, CancellationToken ct)
    {
        var path = SnapshotPath(dir, revision);
        if (!File.Exists(path))
            return null;
        return await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
    }

    private static Task WriteSnapshotAsync(string dir, long revision, string content, CancellationToken ct)
        => AtomicWriteAsync(SnapshotPath(dir, revision), content, ct);

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

    private static async Task<string> ReadAllTextSharedAsync(string fullPath, CancellationToken ct)
    {
        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return await sr.ReadToEndAsync(ct);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record RevisionMeta(
        long CurrentRevision,
        string CurrentHash,
        DateTime UpdatedAtUtc);
}

public sealed record CanonRevisionChange(
    long BaseRevision,
    long EditedRevision,
    string BaseText,
    string EditedText,
    string EditedHash);


