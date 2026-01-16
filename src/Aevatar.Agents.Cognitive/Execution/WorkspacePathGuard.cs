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

        // NOTE:
        // - If user provides an absolute path, we still enforce it must be under root.
        // - If relative, we resolve against root.
        var combined = Path.IsPathRooted(path)
            ? path
            : Path.Combine(root, path);

        var normalized = TryNormalizeFullPath(combined, out var pathError);
        if (normalized == null)
        {
            error = $"path_invalid: '{path}' ({pathError})";
            return false;
        }

        if (!IsWithinRoot(root, normalized))
        {
            error = $"path_out_of_workspace: '{path}' -> '{normalized}' (root='{root}')";
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
}


