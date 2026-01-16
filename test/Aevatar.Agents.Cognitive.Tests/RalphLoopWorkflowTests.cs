using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Execution;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class RalphLoopWorkflowTests
{
    private const string WorkspaceRootEnvVar = "AEVATAR_COGNITIVE_WORKSPACE_ROOT";

    [Fact]
    public async Task RalphLoop_ShouldIterate_UntilVerifierPasses()
    {
        if (OperatingSystem.IsWindows())
            return; // verifier uses sh -c in this test

        var root = MakeTempDir();

        var oldRoot = Environment.GetEnvironmentVariable(WorkspaceRootEnvVar);
        var oldAllow = Environment.GetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar);

        try
        {
            // Workspace root MUST be isolated (avoid touching repo)
            Environment.SetEnvironmentVariable(WorkspaceRootEnvVar, root);
            Environment.SetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar, "sh");

            // Seed a small workspace
            File.WriteAllText(Path.Combine(root, "a.txt"), "aaa\nneedle\nbbb\n");

            // Load workflow from repo YAML
            var parser = new WorkflowParser();
            var wf = parser.ParseFile(Path.Combine(
                RepoRoot(),
                "src",
                "Aevatar.Agents.Cognitive",
                "workflows",
                "ralph-loop.yaml"));

            var coordinator = new TestCognitiveCoordinator();
            coordinator.RegisterWorkflow(wf);

            // Verifier: pass only when ok.txt exists in workspace root
            var variables = new Dictionary<string, object>
            {
                ["goal"] = "Create ok.txt so verifier passes",
                ["success_criteria"] = "Verifier command exits with code 0",
                ["verifier_command"] = "sh",
                ["verifier_args"] = new[] { "-c", "test -f ok.txt" },
                ["verifier_working_dir"] = ".",
                ["max_iterations"] = 5,
                ["max_depth"] = 5
            };

            await coordinator.StartWorkflowAsync("ralph-loop", variables);

            var result = coordinator.GetResult();
            result.Success.ShouldBeTrue(result.Error ?? "workflow failed");

            File.Exists(Path.Combine(root, "ok.txt")).ShouldBeTrue();

            var output = result.Output.ShouldBeOfType<Dictionary<string, object?>>();
            var state = output["state"].ShouldBeOfType<Dictionary<string, object>>();

            state["status"]?.ToString().ShouldBe("completed");

            // iteration increments once per round; we expect 2 rounds: fail then pass
            var iteration = Convert.ToInt32(state["iteration"]);
            iteration.ShouldBe(2);

            var lastVerifier = state["last_verifier"].ShouldBeOfType<Dictionary<string, object>>();
            Convert.ToBoolean(lastVerifier["ok"]).ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspaceRootEnvVar, oldRoot);
            Environment.SetEnvironmentVariable(SandboxCommandExecutor.AllowedCommandsEnvVar, oldAllow);
            TryDeleteDir(root);
        }
    }

    private sealed class TestCognitiveCoordinator : CognitiveCoordinatorGAgent
    {
        private int _proposeCalls;

        public TestCognitiveCoordinator()
        {
            // Avoid requiring Actor runtime in unit tests
            EventPublisher = NullEventPublisher.Instance;
        }

        public override Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var step = (request.StageHint ?? string.Empty).Trim();
            var content = step switch
            {
                "init_state" => @"{
  ""goal"": ""test"",
  ""success_criteria"": ""test"",
  ""iteration"": 0,
  ""max_iterations"": 5,
  ""status"": ""running"",
  ""done"": false,
  ""last_verifier"": null,
  ""last_patch_summary"": """",
  ""diagnostics"": []
}",
                "extract_signals" => @"{
  ""search_pattern"": ""needle"",
  ""file_to_read"": ""a.txt"",
  ""notes"": ""test""
}",
                "propose_patches" => BuildProposePatchesJson(),
                _ => @"{}"
            };

            return Task.FromResult(new ChatResponse { Content = content });
        }

        private string BuildProposePatchesJson()
        {
            _proposeCalls++;

            // Round 1: create a file that does NOT satisfy verifier (ok.txt absent)
            if (_proposeCalls == 1)
            {
                return @"{
  ""summary"": ""round1"",
  ""patches"": [
    { ""op"": ""create_or_replace"", ""path"": ""not_ok.txt"", ""text"": ""nope"" }
  ]
}";
            }

            // Round 2: create ok.txt so verifier passes
            return @"{
  ""summary"": ""round2"",
  ""patches"": [
    { ""op"": ""create_or_replace"", ""path"": ""ok.txt"", ""text"": ""ok"" }
  ]
}";
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found (Directory.Packages.props missing).");
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


