using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Engine;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class YamlLoadTests
{
    private const string WorkspaceRootEnvVar = "AEVATAR_COGNITIVE_WORKSPACE_ROOT";

    [Fact]
    public async Task LoadAgentYaml_ShouldOverrideSystemPrompt_AndExecuteWorkflow()
    {
        var root = MakeTempDir();
        var oldRoot = Environment.GetEnvironmentVariable(WorkspaceRootEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(WorkspaceRootEnvVar, root);

            var sourceAgent = Path.Combine(TestProjectRoot(), "agents", "demo_agent.yaml");
            var targetDir = Path.Combine(root, "aevatar", "agents");
            Directory.CreateDirectory(targetDir);
            File.Copy(sourceAgent, Path.Combine(targetDir, "demo_agent.yaml"), overwrite: true);

            var workflowPath = Path.Combine(TestProjectRoot(), "workflows", "agent_yaml_demo.yaml");
            var parser = new WorkflowParser();
            var wf = parser.ParseFile(workflowPath);

            var coordinator = new TestCoordinator();
            coordinator.RegisterWorkflow(wf);

            await coordinator.StartWorkflowAsync("agent_yaml_demo");

            var result = coordinator.GetResult();
            result.Success.ShouldBeTrue(result.Error ?? "workflow failed");
            coordinator.LastRequest.ShouldNotBeNull();
            coordinator.LastRequest!.Context.TryGetValue("system_prompt", out var sp).ShouldBeTrue();
            sp.ShouldBe("You are a test agent used to validate YAML loading.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspaceRootEnvVar, oldRoot);
            TryDeleteDir(root);
        }
    }

    [Fact]
    public async Task LoadWorkflowYaml_ShouldExecuteWorkflow()
    {
        var path = Path.Combine(TestProjectRoot(), "workflows", "demo_workflow.yaml");
        var parser = new WorkflowParser();
        var wf = parser.ParseFile(path);

        var coordinator = new TestCoordinator();
        coordinator.RegisterWorkflow(wf);

        await coordinator.StartWorkflowAsync("demo_workflow");

        var result = coordinator.GetResult();
        result.Success.ShouldBeTrue(result.Error ?? "workflow failed");
        var output = result.Output.ShouldBeOfType<Dictionary<string, object?>>();
        output["result"]?.ToString().ShouldBe("ok");
    }

    private sealed class TestCoordinator : WorkflowCoordinatorAgent
    {
        public ChatRequest? LastRequest { get; private set; }

        public TestCoordinator()
        {
            EventPublisher = NullEventPublisher.Instance;
        }

        public override Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatResponse { Content = "ok" });
        }
    }

    private static string TestProjectRoot()
        => Path.Combine(RepoRoot(), "test", "Aevatar.Agents.Cognitive.Tests");

    private static string RepoRoot()
    {
        // ============================================================
        //  Repo root discovery (test runs from bin/)
        // ============================================================
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found (Directory.Packages.props missing).");
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
