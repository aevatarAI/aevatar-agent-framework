using System.Linq;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  AttachmentResolver
//
//  说明：
//  - 解析本地路径与模糊匹配（best-effort）
// ============================================================
public static class AttachmentResolver
{
    private const int MaxDepth = 3;

    public static string? ResolvePath(string path, string cwd)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var expanded = ExpandHome(path.Trim());
        var resolved = Path.IsPathRooted(expanded) ? expanded : Path.Combine(cwd, expanded);
        var full = Path.GetFullPath(resolved);

        if (File.Exists(full) || Directory.Exists(full))
            return full;

        return null;
    }

    public static IReadOnlyList<string> FindFuzzyMatches(string root, string token, int maxResults)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(token))
            return list;

        var needle = token.Trim();
        var dirs = new Queue<(string dir, int depth)>();
        dirs.Enqueue((root, 0));

        while (dirs.Count > 0 && list.Count < maxResults)
        {
            var (dir, depth) = dirs.Dequeue();
            if (depth > MaxDepth)
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        list.Add(file);

                    if (list.Count >= maxResults)
                        break;
                }

                if (depth < MaxDepth)
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        dirs.Enqueue((sub, depth + 1));
                }
            }
            catch
            {
                // best-effort
            }
        }

        return list;
    }

    public static string ExpandHome(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
            return home;

        if (path[1] == Path.DirectorySeparatorChar || path[1] == Path.AltDirectorySeparatorChar)
            return Path.Combine(home, path[2..]);

        return path;
    }
}


