using System.Diagnostics.CodeAnalysis;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  Workspace Path Guard
//
//  Responsibility:
//  - Resolve a host-controlled workspace root
//  - Enforce "within root" path safety for all deterministic primitives
//
//  Security:
//  - Never accept workspace root from workflow inputs (only env / safe fallback)
//  - Always normalize paths via Path.GetFullPath before checking prefix
// ============================================================

internal static class WorkspacePathGuard
{
    public const string WorkspaceRootEnvVar = "AEVATAR_COGNITIVE_WORKSPACE_ROOT";

    public static bool TryGetWorkspaceRoot(
        [NotNullWhen(true)] out string? workspaceRoot,
        [NotNullWhen(false)] out string? error)
    {
        workspaceRoot = null;
        error = null;

        var configured = (Environment.GetEnvironmentVariable(WorkspaceRootEnvVar) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = TryNormalizeFullPath(configured, out var normalizeError);
            if (full == null)
            {
                error = $"workspace_root_invalid: env {WorkspaceRootEnvVar}='{configured}' ({normalizeError})";
                return false;
            }

            if (!Directory.Exists(full))
            {
                error = $"workspace_root_missing: env {WorkspaceRootEnvVar}='{full}' (directory not found)";
                return false;
            }

            workspaceRoot = full;
            return true;
        }

        // Dev/test fallback: locate repo root by searching for Directory.Packages.props
        var fallback = TryFindRepoRoot();
        if (fallback == null)
        {
            error = $"workspace_root_missing: set env {WorkspaceRootEnvVar} or run within a repo containing Directory.Packages.props";
            return false;
        }

        workspaceRoot = fallback;
        return true;
    }

    public static bool TryResolvePathWithinRoot(
        string workspaceRoot,
        string path,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out string? error)
    {
        fullPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            error = "workspace_root_invalid: empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_invalid: empty";
            return false;
        }

        var root = TryNormalizeFullPath(workspaceRoot, out var rootError);
        if (root == null)
        {
            error = $"workspace_root_invalid: {rootError}";
            return false;
        }

        var expandedPath = ExpandHome(path);

        // NOTE:
        // - If user provides an absolute path, we still enforce it must be under allowed roots.
        // - If relative, we resolve against workspace root.
        var combined = Path.IsPathRooted(expandedPath)
            ? expandedPath
            : Path.Combine(root, expandedPath);

        var normalized = TryNormalizeFullPath(combined, out var pathError);
        if (normalized == null)
        {
            error = $"path_invalid: '{path}' ({pathError})";
            return false;
        }

        var allowedRoots = GetAllowedRoots(root);
        if (!IsWithinAnyRoot(allowedRoots, normalized))
        {
            var allowed = string.Join("|", allowedRoots);
            error = $"path_out_of_workspace: '{path}' -> '{normalized}' (allowed='{allowed}')";
            return false;
        }

        fullPath = normalized;
        return true;
    }

    public static bool IsWithinRoot(string workspaceRoot, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(fullPath))
            return false;

        var root = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = root + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, root, StringComparison.Ordinal)
               || fullPath.StartsWith(rootPrefix, StringComparison.Ordinal);
    }

    private static bool IsWithinAnyRoot(IReadOnlyList<string> roots, string fullPath)
    {
        foreach (var root in roots)
        {
            if (IsWithinRoot(root, fullPath))
                return true;
        }
        return false;
    }

    private static string? TryFindRepoRoot()
    {
        // ============================================================
        //  Repo root discovery
        //
        //  WHY:
        //  - Tests run from bin/ and need a stable monorepo anchor.
        //  - This fallback is only for dev/test; production should set env.
        // ============================================================
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    private static string? TryNormalizeFullPath(string path, out string error)
    {
        error = string.Empty;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static List<string> GetAllowedRoots(string workspaceRoot)
    {
        var roots = new List<string>();
        TryAddRoot(roots, workspaceRoot);

        var configDir = ResolveAevatarConfigDirectory();
        TryAddRoot(roots, configDir);

        return roots;
    }

    private static void TryAddRoot(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var expanded = ExpandHome(path);
        var normalized = TryNormalizeFullPath(expanded, out _);
        if (normalized == null)
            return;

        normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!roots.Contains(normalized, StringComparer.Ordinal))
            roots.Add(normalized);
    }

    private static string ResolveAevatarConfigDirectory()
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_DIR") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var secretsDir = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_DIR") ?? string.Empty).Trim();
        if (secretsDir.Length > 0)
            return ExpandHome(secretsDir);

        var secretsPath = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS_PATH") ?? string.Empty).Trim();
        if (secretsPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(secretsPath)) ?? ExpandHome(secretsPath);

        var legacySecrets = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (legacySecrets.Length > 0)
            return Path.GetDirectoryName(ExpandHome(legacySecrets)) ?? ExpandHome(legacySecrets);

        var configPath = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG") ?? string.Empty).Trim();
        if (configPath.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath)) ?? ExpandHome(configPath);

        var configPath2 = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_PATH") ?? string.Empty).Trim();
        if (configPath2.Length > 0)
            return Path.GetDirectoryName(ExpandHome(configPath2)) ?? ExpandHome(configPath2);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    private static string ExpandHome(string path)
    {
        var p = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return p;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }
}


