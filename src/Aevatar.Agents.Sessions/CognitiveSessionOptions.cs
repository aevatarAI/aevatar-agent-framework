using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Aevatar.Agents.Sessions;

public sealed class CognitiveSessionOptions
{
    /// <summary>
    /// Workflow directory (defaults to ~/.aevatar/workflows).
    /// </summary>
    public string? WorkflowsDirectory { get; set; }

    /// <summary>
    /// Whether to lazily create role agents.
    /// </summary>
    public bool LazyLoadRoles { get; set; } = true;
}

/// <summary>
/// Workflow directory resolution helpers for app bootstrapping.
/// </summary>
public static class CognitiveSessionWorkflows
{
    // 中文 + ASCII:
    // - 约定优先：当前 ContentRoot 的 workflows/
    // - 兼容单仓：apps/*/src/{AppName}/workflows 与 src/{AppName}/workflows
    // - 只做目录存在性判断，不读取文件内容
    public static string ResolveWorkflowsDirectory(string contentRootPath, string? applicationName)
    {
        return ResolveWorkflowsDirectory(contentRootPath, applicationName, workflowName: null);
    }

    private static string NormalizeAppName(string? applicationName)
    {
        var name = (applicationName ?? string.Empty).Trim();
        if (name.Length > 0)
            return name;

        var entry = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        return entry.Trim();
    }

    public static string ResolveWorkflowsDirectory(
        string contentRootPath,
        string? applicationName,
        string? workflowName)
    {
        var root = NormalizeRoot(contentRootPath);
        var appName = NormalizeAppName(applicationName);
        var resolved = ResolveDirectoryFromRoot(root, appName, workflowName);
        if (resolved != null)
            return resolved;

        var current = new DirectoryInfo(root);
        for (var i = 0; i < 6 && current.Parent != null; i++)
        {
            current = current.Parent;
            resolved = ResolveDirectoryFromRoot(current.FullName, appName, workflowName);
            if (resolved != null)
                return resolved;
        }

        var fallback = BuildCandidates(root, appName, includeAllApps: !string.IsNullOrWhiteSpace(workflowName))
            .FirstOrDefault();
        return fallback ?? Path.Combine(root, "workflows");
    }

    public static string? TryResolveWorkflowPath(
        string workflowName,
        string contentRootPath,
        string? applicationName)
    {
        var name = NormalizeWorkflowName(workflowName);
        if (name.Length == 0)
            return null;

        if (File.Exists(name))
            return Path.GetFullPath(name);

        var root = NormalizeRoot(contentRootPath);
        var appName = NormalizeAppName(applicationName);
        var resolved = ResolveWorkflowPathFromRoot(root, appName, name);
        if (resolved != null)
            return resolved;

        var current = new DirectoryInfo(root);
        for (var i = 0; i < 6 && current.Parent != null; i++)
        {
            current = current.Parent;
            resolved = ResolveWorkflowPathFromRoot(current.FullName, appName, name);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    private static string NormalizeRoot(string contentRootPath)
    {
        return string.IsNullOrWhiteSpace(contentRootPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(contentRootPath);
    }

    private static string NormalizeWorkflowName(string? workflowName)
        => (workflowName ?? string.Empty).Trim();

    private static string? ResolveDirectoryFromRoot(string root, string appName, string? workflowName)
    {
        var includeAllApps = !string.IsNullOrWhiteSpace(workflowName);
        var candidates = BuildCandidates(root, appName, includeAllApps);
        if (string.IsNullOrWhiteSpace(workflowName))
            return FindFirstExisting(candidates);

        return FindFirstContaining(candidates, workflowName!.Trim());
    }

    private static string? ResolveWorkflowPathFromRoot(string root, string appName, string workflowName)
    {
        var candidates = BuildCandidates(root, appName, includeAllApps: true);
        foreach (var candidate in candidates)
        {
            if (TryResolveInDirectory(candidate, workflowName, out var path))
                return path;
        }

        return null;
    }

    private static List<string> BuildCandidates(string root, string appName, bool includeAllApps)
    {
        var list = new List<string> { Path.Combine(root, "workflows") };
        if (appName.Length > 0)
        {
            list.Add(Path.Combine(root, "src", appName, "workflows"));
            AppendAppsByName(list, root, appName);
        }

        if (includeAllApps)
        {
            AppendSrcApps(list, root);
            AppendAppsAnyName(list, root);
        }

        return Deduplicate(list);
    }

    private static string? FindFirstExisting(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? FindFirstContaining(IEnumerable<string> candidates, string workflowName)
    {
        foreach (var candidate in candidates)
        {
            if (TryResolveInDirectory(candidate, workflowName, out _))
                return candidate;
        }

        return null;
    }

    private static bool TryResolveInDirectory(string dir, string workflowName, out string path)
    {
        path = string.Empty;
        if (!Directory.Exists(dir))
            return false;

        var direct = Path.Combine(dir, workflowName);
        if (File.Exists(direct))
        {
            path = Path.GetFullPath(direct);
            return true;
        }

        if (Path.HasExtension(workflowName))
            return false;

        var yaml = Path.Combine(dir, $"{workflowName}.yaml");
        if (File.Exists(yaml))
        {
            path = Path.GetFullPath(yaml);
            return true;
        }

        var yml = Path.Combine(dir, $"{workflowName}.yml");
        if (File.Exists(yml))
        {
            path = Path.GetFullPath(yml);
            return true;
        }

        var json = Path.Combine(dir, $"{workflowName}.json");
        if (File.Exists(json))
        {
            path = Path.GetFullPath(json);
            return true;
        }

        return false;
    }

    private static void AppendAppsByName(List<string> list, string root, string appName)
    {
        var appsRoot = Path.Combine(root, "apps");
        if (!Directory.Exists(appsRoot))
            return;

        foreach (var appDir in Directory.EnumerateDirectories(appsRoot))
        {
            list.Add(Path.Combine(appDir, "src", appName, "workflows"));
        }
    }

    private static void AppendSrcApps(List<string> list, string root)
    {
        var srcRoot = Path.Combine(root, "src");
        if (!Directory.Exists(srcRoot))
            return;

        foreach (var appDir in Directory.EnumerateDirectories(srcRoot))
        {
            list.Add(Path.Combine(appDir, "workflows"));
        }
    }

    private static void AppendAppsAnyName(List<string> list, string root)
    {
        var appsRoot = Path.Combine(root, "apps");
        if (!Directory.Exists(appsRoot))
            return;

        foreach (var appDir in Directory.EnumerateDirectories(appsRoot))
        {
            var appSrc = Path.Combine(appDir, "src");
            if (!Directory.Exists(appSrc))
                continue;

            foreach (var appSrcDir in Directory.EnumerateDirectories(appSrc))
            {
                list.Add(Path.Combine(appSrcDir, "workflows"));
            }
        }
    }

    private static List<string> Deduplicate(IEnumerable<string> candidates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (set.Add(candidate))
                list.Add(candidate);
        }

        return list;
    }
}
