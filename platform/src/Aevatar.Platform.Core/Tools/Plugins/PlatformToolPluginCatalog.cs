using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Workflow;

namespace Aevatar.Platform.Core.Tools.Plugins;

// ============================================================
//  PlatformToolPluginCatalog
//
//  说明：
//  - 解析工具插件目录（优先用户配置，其次默认目录）
//  - 默认目录：
//    - ~/.aevatar/tools
//    - ./aevatar/tools
// ============================================================
public sealed class PlatformToolPluginCatalog
{
    public IReadOnlyList<string> ResolveToolDirectories(WorkflowRunInput input, ToolsConfig tools)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tools);

        var working = string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : input.WorkingDirectory;

        var configDir = string.IsNullOrWhiteSpace(input.ConfigDirectory)
            ? Directory.GetCurrentDirectory()
            : input.ConfigDirectory;

        var rawDirs = tools.Plugins.Directories ?? new List<string>();
        if (rawDirs.Count == 0)
        {
            rawDirs = new List<string>
            {
                Path.Combine(configDir, "tools"),
                Path.Combine(working, "aevatar", "tools")
            };
        }

        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in rawDirs)
        {
            var resolvedPath = ResolvePath(raw, working, configDir);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                continue;

            if (seen.Add(resolvedPath))
                resolved.Add(resolvedPath);
        }

        return resolved;
    }

    private static string ResolvePath(string? raw, string working, string configDir)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        var expanded = ExpandHome(trimmed);
        if (!Path.IsPathRooted(expanded))
        {
            var baseDir = IsWorkingRelative(expanded) ? working : configDir;
            expanded = Path.Combine(baseDir, expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsWorkingRelative(string value)
    {
        return value.StartsWith("./", StringComparison.Ordinal) ||
               value.StartsWith(".\\", StringComparison.Ordinal);
    }

    private static string ExpandHome(string path)
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
