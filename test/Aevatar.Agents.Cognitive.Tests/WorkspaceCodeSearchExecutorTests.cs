using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class WorkspaceCodeSearchExecutorTests
{
    [Fact]
    public void Execute_ShouldFindMatch_AndIncludeContext()
    {
        var root = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "aaa\nneedle\nbbb\n");

            var exec = new WorkspaceCodeSearchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "search",
                Type = "workspace_code_search",
                Parameters = new Dictionary<string, object?>
                {
                    ["pattern"] = "needle",
                    ["context_lines"] = 1,
                    ["max_results"] = 10
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);

            var matches = dict["matches"].ShouldBeOfType<List<object>>();
            matches.Count.ShouldBeGreaterThan(0);

            var m0 = matches[0].ShouldBeOfType<Dictionary<string, object?>>();
            m0["path"].ShouldBe("a.txt");
            m0["line"].ShouldBe(2);

            var excerpt = m0["excerpt"]?.ToString() ?? string.Empty;
            excerpt.ShouldContain(">2: needle");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldCapResults_AndSetTruncated()
    {
        var root = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "needle\nneedle\nneedle\n");

            var exec = new WorkspaceCodeSearchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "search",
                Type = "workspace_code_search",
                Parameters = new Dictionary<string, object?>
                {
                    ["pattern"] = "needle",
                    ["context_lines"] = 0,
                    ["max_results"] = 1
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);
            dict["truncated"].ShouldBe(true);

            var matches = dict["matches"].ShouldBeOfType<List<object>>();
            matches.Count.ShouldBe(1);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldReturnError_WhenPatternMissing()
    {
        var root = MakeTempDir();
        try
        {
            var exec = new WorkspaceCodeSearchExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "search",
                Type = "workspace_code_search",
                Parameters = new Dictionary<string, object?>()
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["error"].ShouldBe("missing_required_param:pattern");
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


