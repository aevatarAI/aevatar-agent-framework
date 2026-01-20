namespace Aevatar.Platform.Cli.Commands;

// ============================================================
//  RepoPathResolver - 仓库路径解析工具
//
//  WHY:
//  - 避免命令层手写“向上找 repo root”的重复逻辑
//  - 统一默认路径推导策略（如 workflows）
// ============================================================
internal static class RepoPathResolver
{
    private const string WorkflowsRelativePath = "src/Aevatar.Agents.Cognitive/workflows";

    public static string? ResolveWorkflowsSourceDirectory(string? sourceDir, string? startDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(sourceDir))
            return sourceDir.Trim();

        var root = FindRepoRoot(startDirectory ?? Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var candidate = Path.Combine(root, WorkflowsRelativePath);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return current.FullName;
            current = current.Parent;
        }

        return null;
    }
}
