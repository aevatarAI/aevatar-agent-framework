using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class SandboxCommandExecutorTests
{
    [Fact]
    public void Execute_ShouldRunCommand_AndReturnOk()
    {
        var root = MakeTempDir();
        try
        {
            var exec = new SandboxCommandExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "verify",
                Type = "sandbox_command",
                Parameters = new Dictionary<string, object?>
                {
                    ["command"] = "dotnet",
                    ["args"] = new[] { "--version" },
                    ["working_dir"] = ".",
                    ["timeout_ms"] = 10_000,
                    ["max_output_chars"] = 4_000
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(true);
            dict["timed_out"].ShouldBe(false);
            dict["exit_code"].ShouldBe(0);

            (dict["stdout"]?.ToString() ?? string.Empty).Length.ShouldBeGreaterThan(0);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldTimeout()
    {
        if (OperatingSystem.IsWindows())
            return; // keep CI portable

        var root = MakeTempDir();
        try
        {
            var exec = new SandboxCommandExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "verify",
                Type = "sandbox_command",
                Parameters = new Dictionary<string, object?>
                {
                    ["command"] = "sh",
                    ["args"] = new[] { "-c", "sleep 2" },
                    ["working_dir"] = ".",
                    ["timeout_ms"] = 100,
                    ["max_output_chars"] = 2_000
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["timed_out"].ShouldBe(true);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Execute_ShouldDeny_WhenAllowlistSet()
    {
        var root = MakeTempDir();
        var old = Environment.GetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar, "echo");

            var exec = new SandboxCommandExecutor(new TemplateEngine(), NullLogger.Instance);
            var step = new StepDefinition
            {
                Id = "verify",
                Type = "sandbox_command",
                Parameters = new Dictionary<string, object?>
                {
                    ["command"] = "dotnet",
                    ["args"] = new[] { "--version" },
                    ["working_dir"] = ".",
                    ["timeout_ms"] = 10_000,
                    ["max_output_chars"] = 2_000
                }
            };

            var result = exec.Execute(step, new Dictionary<string, object>(), root);
            result.Success.ShouldBeTrue();

            var dict = result.Value.ShouldBeOfType<Dictionary<string, object?>>();
            dict["ok"].ShouldBe(false);
            dict["error"].ShouldBe("command_denied");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar, old);
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


