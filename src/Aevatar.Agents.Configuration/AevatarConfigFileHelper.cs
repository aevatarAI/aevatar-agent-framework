namespace Aevatar.Agents.Configuration;

// ============================================================
//  AevatarConfigFileHelper
//
//  说明：
//  - 统一处理 ~/.aevatar 下的目录定位与文件解析
//  - 通过 enum 选择子目录，避免硬编码散落
// ============================================================
public static class AevatarConfigFileHelper
{
    public static string ResolveConfigRoot(string? configRoot = null)
    {
        var root = (configRoot ?? string.Empty).Trim();
        if (root.Length > 0)
            return Path.GetFullPath(root);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    public static string GetDirectoryPath(
        string? configRoot,
        AevatarConfigDirectory directory)
    {
        var root = ResolveConfigRoot(configRoot);
        return directory switch
        {
            AevatarConfigDirectory.Root => root,
            AevatarConfigDirectory.Agents => Path.Combine(root, "agents"),
            AevatarConfigDirectory.Workflows => Path.Combine(root, "workflows"),
            AevatarConfigDirectory.Tools => Path.Combine(root, "tools"),
            AevatarConfigDirectory.Sessions => Path.Combine(root, "sessions"),
            AevatarConfigDirectory.Mcp => Path.Combine(root, "mcp"),
            _ => root
        };
    }

    public static string? ResolveFilePath(
        string? configRoot,
        AevatarConfigDirectory directory,
        string? name,
        IReadOnlyList<string> extensions)
    {
        var fileName = SanitizeFileName(name);
        if (fileName.Length == 0)
            return null;

        var dir = GetDirectoryPath(configRoot, directory);
        var normalizedExts = NormalizeExtensions(extensions);

        if (normalizedExts.Count == 0)
        {
            var path = Path.Combine(dir, fileName);
            return File.Exists(path) ? path : null;
        }

        var ext = NormalizeExtension(Path.GetExtension(fileName));
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (ext.Length > 0 && normalizedExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(dir, $"{baseName}{ext}");
            if (File.Exists(path))
                return path;
        }

        foreach (var candidate in normalizedExts)
        {
            var path = Path.Combine(dir, $"{baseName}{candidate}");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static IReadOnlyList<string> ListFileBaseNames(
        string? configRoot,
        AevatarConfigDirectory directory,
        IReadOnlyList<string> extensions)
    {
        try
        {
            var dir = GetDirectoryPath(configRoot, directory);
            if (!Directory.Exists(dir))
                return Array.Empty<string>();

            var normalizedExts = NormalizeExtensions(extensions);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = NormalizeExtension(Path.GetExtension(file));
                if (normalizedExts.Count > 0 && !normalizedExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var trimmed = name.Trim();
        var safe = Path.GetFileName(trimmed);
        return safe ?? string.Empty;
    }

    private static IReadOnlyList<string> NormalizeExtensions(IReadOnlyList<string> extensions)
    {
        if (extensions == null || extensions.Count == 0)
            return Array.Empty<string>();

        var list = new List<string>(extensions.Count);
        foreach (var raw in extensions)
        {
            var ext = NormalizeExtension(raw);
            if (ext.Length > 0)
                list.Add(ext);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeExtension(string? ext)
    {
        var value = (ext ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;
        return value.StartsWith('.') ? value : "." + value;
    }
}
