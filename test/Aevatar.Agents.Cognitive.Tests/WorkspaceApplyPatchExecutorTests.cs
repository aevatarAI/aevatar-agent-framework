using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class WorkspaceApplyPatchExecutorTests
{
    [Fact]
    public void Execute_CreateOrReplace_ShouldWriteFile()
    {
        var root = MakeTempDir();
        try
        {
            var patches = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["op"] = "create_or_replace",
                    ["path"] = "x.txt",
                    ["text"] = "hello"
                }
            };

            var vars = new Dictionary<string, object>
            {
                ["plan"] = new Dictionary<string, object> { ["patches"] = patches }
            };

            var exec = new WorkspaceApplyPatchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "apply",
                Type = "workspace_apply_patch",
                Parameters = new Dictionary<string, object?>
                {
                    ["patches"] = "{{ plan.patches }}"
                }
            };

            var result = exec.Execute(step, vars, root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);
            dict["files_changed"].ShouldBe(1);
            dict["patches_applied"].ShouldBe(1);

            File.ReadAllText(Path.Combine(root, "x.txt")).ShouldBe("hello");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ReplaceSpan_ShouldReplaceLines()
    {
        var root = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "1\n2\n3\n");

            var patches = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["op"] = "replace_span",
                    ["path"] = "a.txt",
                    ["start_line"] = 2,
                    ["end_line_exclusive"] = 3,
                    ["replace_text"] = "X"
                }
            };

            var exec = new WorkspaceApplyPatchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "apply",
                Type = "workspace_apply_patch",
                Parameters = new Dictionary<string, object?>
                {
                    ["patches"] = patches
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);

            File.ReadAllText(Path.Combine(root, "a.txt")).Replace("\r", "").ShouldBe("1\nX\n3\n");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldRejectPathEscape()
    {
        var root = MakeTempDir();
        try
        {
            var patches = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["op"] = "create_or_replace",
                    ["path"] = "../escape.txt",
                    ["text"] = "nope"
                }
            };

            var exec = new WorkspaceApplyPatchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "apply",
                Type = "workspace_apply_patch",
                Parameters = new Dictionary<string, object?>
                {
                    ["patches"] = patches
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["patches_applied"].ShouldBe(0);

            var escaped = Path.GetFullPath(Path.Combine(root, "..", "escape.txt"));
            File.Exists(escaped).ShouldBeFalse();
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aevatar_cognitive_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}


