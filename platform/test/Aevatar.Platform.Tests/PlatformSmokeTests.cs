using System.CommandLine;
using System.CommandLine.Parsing;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Platform;
using Aevatar.Platform.Cli.Commands;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Sessions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;

namespace Aevatar.Platform.Tests;

public sealed class PlatformSmokeTests
{
    [Fact]
    public void Cli_has_expected_commands()
    {
        var root = RootCommands.BuildRootCommand();
        var names = root.Children
            .OfType<Command>()
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("run", names);
        Assert.Contains("tui", names);
        Assert.Contains("serve", names);
        Assert.Contains("web", names);
        Assert.Contains("attach", names);
        Assert.Contains("sessions", names);
        Assert.Contains("config", names);
        Assert.Contains("agents", names);
        Assert.Contains("workflows", names);
        Assert.Contains("models", names);
        Assert.Contains("mcp", names);
        Assert.Contains("auth", names);
        Assert.Contains("github", names);
        Assert.Contains("stats", names);
        Assert.Contains("export", names);
        Assert.Contains("import", names);
        Assert.Contains("acp", names);
        Assert.Contains("upgrade", names);
        Assert.Contains("uninstall", names);
    }

    [Fact]
    public void Cli_parse_accepts_basic_flags()
    {
        var parser = RootCommands.BuildParser();

        var result = parser.Parse(new[] { "--workflow", "hermes", "--profile", "coding", "--provider", "openai", "--command", "hi" });
        Assert.Empty(result.Errors);

        var result2 = parser.Parse(new[] { "sessions", "list" });
        Assert.Empty(result2.Errors);

        var result3 = parser.Parse(new[] { "run", "--continue", "--session", "abc", "hello" });
        Assert.Empty(result3.Errors);
    }

    [Fact]
    public void Dsl_compile_success()
    {
        var compiler = new PlatformMeshCompiler();
        var json = """
                   {
                     "dsl_version": "0.1",
                     "goal": { "name": "test" },
                     "strategy": "cot",
                     "budget": { "max_steps": 1, "token_limit": 100 },
                     "nodes": [
                       { "id": "a", "type": "DivergentAgent" },
                       { "id": "b", "type": "ConvergentAgent" }
                     ],
                     "edges": [
                       { "from": "a", "to": "b", "channel": "upstream_output" }
                     ],
                     "constraints": []
                   }
                   """;

        var result = compiler.Compile(json);
        Assert.True(result.Ok);
        Assert.NotNull(result.Definition);
    }

    [Fact]
    public void Dsl_compile_rejects_unknown_agent_type()
    {
        var compiler = new PlatformMeshCompiler();
        var json = """
                   {
                     "dsl_version": "0.1",
                     "goal": { "name": "test" },
                     "strategy": "cot",
                     "budget": { "max_steps": 1, "token_limit": 100 },
                     "nodes": [
                       { "id": "a", "type": "UnknownAgent" }
                     ],
                     "edges": [],
                     "constraints": []
                   }
                   """;

        var result = compiler.Compile(json);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Code == "node.unsupported_type");
    }

    [Fact]
    public async Task Session_export_import_roundtrip()
    {
        var root = CreateTempDir();
        try
        {
            var sessionsDir = Path.Combine(root, "sessions");
            var store = new FileEventStore(sessionsDir);
            var service = new SessionService(store, root);

            var state = new PlatformSessionState
            {
                SessionId = string.Empty,
                Profile = "coding",
                ActiveWorkflow = "hermes",
                WorkingDirectory = root,
                Provider = "test",
                Model = "test-model"
            };

            var sessionId = await service.CreateSessionAsync(state);
            var evt = new PlatformSessionEvent
            {
                Seq = 1,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                UserMessage = new UserMessageEvent
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    Text = "hello"
                }
            };

            await service.AppendEventAsync(sessionId, evt);

            var events = await service.GetSessionEventsAsync(sessionId);
            Assert.Single(events);

            var exportPath = Path.Combine(root, "export.json");
            await service.ExportSessionAsync(sessionId, exportPath);

            var importedId = await service.ImportSessionAsync(exportPath);
            var importedEvents = await service.GetSessionEventsAsync(importedId);
            Assert.Equal(events.Count, importedEvents.Count);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Tool_policy_denies_by_default_and_allows_allowlist()
    {
        var config = new AevatarConfig();
        config.Tools.Shell.AllowedCommands.Add("git");

        var temp = CreateTempDir();
        try
        {
            config.Tools.FileSystem.AllowedPaths.Add(temp);
            var policy = PlatformToolPolicy.Create(config, "vibe_default");

            Assert.False(policy.TryValidateCommand("rm -rf /", out _));
            Assert.True(policy.TryValidateCommand("git status", out _));

            Assert.True(policy.TryValidatePath(temp, out _, out _));

            var tool = new ToolDefinition
            {
                Name = "shell",
                IsDangerous = true
            };

            var decision = policy.EvaluateTool(tool);
            Assert.False(decision.Allowed);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aevatar_platform_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}


