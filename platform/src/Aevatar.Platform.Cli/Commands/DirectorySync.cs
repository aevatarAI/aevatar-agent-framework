namespace Aevatar.Platform.Cli.Commands;

// ============================================================
//  DirectorySync - 目录同步小工具
//
//  WHY:
//  - 统一目录同步的路径校验与备份逻辑
//  - 避免各命令重复“source/target 校验 + backup + copy”
// ============================================================
internal static class DirectorySync
{
    internal enum ResolveError
    {
        None,
        SourceNotFound,
        SameDirectory
    }

    internal sealed record SyncResult(
        int Copied,
        List<(string FileName, string LegacyPath)> Replaced);

    public static bool TryResolvePaths(
        string? sourceDir,
        string targetDir,
        out string sourceFull,
        out ResolveError error)
    {
        sourceFull = string.Empty;
        error = ResolveError.None;

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            error = ResolveError.SourceNotFound;
            return false;
        }

        sourceFull = Path.GetFullPath(sourceDir);
        var targetFull = Path.GetFullPath(targetDir);
        if (string.Equals(
                sourceFull.TrimEnd(Path.DirectorySeparatorChar),
                targetFull.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            error = ResolveError.SameDirectory;
            return false;
        }

        return true;
    }

    public static SyncResult SyncFiles(
        string sourceDir,
        string targetDir,
        string timestamp,
        Func<string, bool> includeFile)
    {
        var replaced = new List<(string FileName, string LegacyPath)>();
        var copied = 0;

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (!includeFile(file))
                continue;

            var fileName = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var destPath = Path.Combine(targetDir, fileName);
            if (File.Exists(destPath))
            {
                var legacyPath = FileBackup.MoveToLegacy(destPath, timestamp);
                replaced.Add((fileName, legacyPath));
            }

            File.Copy(file, destPath, overwrite: true);
            copied++;
        }

        return new SyncResult(copied, replaced);
    }
}
