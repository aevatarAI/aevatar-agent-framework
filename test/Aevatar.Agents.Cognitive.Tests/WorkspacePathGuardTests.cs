using Aevatar.Agents.Cognitive.Execution;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class WorkspacePathGuardTests
{
    private const string ConfigDirEnv = "AEVATAR_CONFIG_DIR";

    [Fact]
    public void TryResolvePathWithinRoot_ShouldAllowConfigDirAbsolutePath()
    {
        var workspaceRoot = MakeTempDir();
        var configDir = MakeTempDir();
        var targetPath = Path.Combine(configDir, "agents", "demo.yaml");

        var oldEnv = Environment.GetEnvironmentVariable(ConfigDirEnv);
        try
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, configDir);

            WorkspacePathGuard.TryResolvePathWithinRoot(
                workspaceRoot,
                targetPath,
                out var fullPath,
                out var error).ShouldBeTrue(error);

            fullPath.ShouldBe(Path.GetFullPath(targetPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, oldEnv);
            TryDeleteDir(workspaceRoot);
            TryDeleteDir(configDir);
        }
    }

    [Fact]
    public void TryResolvePathWithinRoot_ShouldExpandHome_ForAevatarPaths()
    {
        var workspaceRoot = MakeTempDir();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(home, ".aevatar");
        var targetPath = "~/.aevatar/workflows/agent_router.yaml";

        var oldEnv = Environment.GetEnvironmentVariable(ConfigDirEnv);
        try
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, configDir);

            WorkspacePathGuard.TryResolvePathWithinRoot(
                workspaceRoot,
                targetPath,
                out var fullPath,
                out var error).ShouldBeTrue(error);

            fullPath.ShouldBe(Path.GetFullPath(Path.Combine(home, ".aevatar", "workflows", "agent_router.yaml")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConfigDirEnv, oldEnv);
            TryDeleteDir(workspaceRoot);
        }
    }

    private static string MakeTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aevatar_cognitive_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
