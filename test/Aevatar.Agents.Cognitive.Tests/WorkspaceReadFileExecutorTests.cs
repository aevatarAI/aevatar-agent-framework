using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class WorkspaceReadFileExecutorTests
{
    [Fact]
    public void Execute_ShouldRejectPathEscape()
    {
        var root = MakeTempDir();
        try
        {
            var exec = new WorkspaceReadFileExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "read",
                Type = "workspace_read_file",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = "../escape.txt",
                    ["max_chars"] = 128
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["error"].ShouldNotBeNull();
            (dict["error"]!.ToString() ?? string.Empty).ShouldContain("path_out_of_workspace");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldTruncate_WhenExceedsMaxChars()
    {
        var root = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello world");

            var exec = new WorkspaceReadFileExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "read",
                Type = "workspace_read_file",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = "a.txt",
                    ["max_chars"] = 5
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);
            dict["content"].ShouldBe("hello");
            dict["truncated"].ShouldBe(true);
            dict["kept_chars"].ShouldBe(5);
            dict["total_chars"].ShouldBe(11);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldReturnError_WhenMissingFile()
    {
        var root = MakeTempDir();
        try
        {
            var exec = new WorkspaceReadFileExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "read",
                Type = "workspace_read_file",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = "missing.txt",
                    ["max_chars"] = 128
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["error"].ShouldBe("file_not_found");
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


