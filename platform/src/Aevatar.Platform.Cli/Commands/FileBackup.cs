namespace Aevatar.Platform.Cli.Commands;

// ============================================================
//  FileBackup - 统一文件备份工具
//
//  WHY:
//  - 避免在各处散落“_legacy_时间”命名逻辑
//  - 统一处理重名冲突与兜底策略
// ============================================================
internal static class FileBackup
{
    private const int MaxSuffixAttempts = 1000;

    public static string MoveToLegacy(string path, string timestamp, string tag = "legacy")
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(timestamp))
            throw new ArgumentException("timestamp is required.", nameof(timestamp));
        if (!File.Exists(path))
            return path;

        var legacyPath = BuildLegacyPath(path, timestamp, tag);
        File.Move(path, legacyPath);
        return legacyPath;
    }

    public static string BuildLegacyPath(string path, string timestamp, string tag = "legacy")
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(timestamp))
            throw new ArgumentException("timestamp is required.", nameof(timestamp));

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var baseName = $"{name}_{tag}_{timestamp}";
        var legacyPath = Path.Combine(dir, $"{baseName}{ext}");
        if (!File.Exists(legacyPath))
            return legacyPath;

        for (var i = 2; i < MaxSuffixAttempts; i++)
        {
            legacyPath = Path.Combine(dir, $"{baseName}_{i}{ext}");
            if (!File.Exists(legacyPath))
                return legacyPath;
        }

        return Path.Combine(dir, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }
}
