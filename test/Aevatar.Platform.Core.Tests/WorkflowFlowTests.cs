using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Platform.Core.Tools;
using Aevatar.Platform.Core.Workflow;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Platform.Core.Tests;

public class WorkflowFlowTests
{
    [Fact]
    public async Task MeshNormalizeTool_ShouldReturnCanonicalJson()
    {
        var raw = """
dsl_version: "0.1"
goal:
  name: "greeting"
  success_metric: "user greeted"
strategy: "cot"
budget:
  max_steps: 3
  token_limit: 500
nodes:
  - id: "worker"
    type: "WorkerAgent"
edges: []
constraints: []
""";

        var tool = new MeshNormalizeTool(configDirectory: string.Empty, workingDirectory: Directory.GetCurrentDirectory());
        var parameters = new Dictionary<string, object>
        {
            ["content"] = raw,
            ["format"] = "json"
        };

        var result = await tool.ExecuteAsync(parameters, new ToolContext(), logger: null, CancellationToken.None);
        var payload = JsonFormatter.Default.Format((Struct)result);

        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        doc.RootElement.GetProperty("format").GetString().ShouldBe("json");

        var normalized = doc.RootElement.GetProperty("normalized").GetString();
        normalized.ShouldNotBeNullOrWhiteSpace();

        var compiler = new PlatformMeshCompiler();
        var compile = compiler.Compile(normalized!);
        compile.Ok.ShouldBeTrue();
        compile.Definition!.Nodes.Count.ShouldBe(1);
    }

    [Fact]
    public void RoleBasedWorkflow_ShouldCompileAndPlan()
    {
        using var temp = new TempWorkspace();
        var agentsDir = Path.Combine(temp.Root, "agents");
        Directory.CreateDirectory(agentsDir);

        var agentPath = Path.Combine(agentsDir, "translator.yaml");
        File.WriteAllText(agentPath, """
name: translator
description: Minimal translator role
instructions: |
  Translate between Chinese and English.
""");

        var raw = """
dsl_version: "0.1"
goal:
  name: "translation"
  success_metric: "accurate translation"
strategy: "cot"
budget:
  max_steps: 5
  token_limit: 3000
nodes:
  - id: "translator"
    type: "translator"
edges: []
constraints: []
""";

        var compiler = new PlatformMeshCompiler(configAgentsDir: agentsDir);
        var compile = compiler.Compile(raw);
        compile.Ok.ShouldBeTrue();

        var plan = new WorkflowEngine().Plan(compile.Definition!);
        plan.Ok.ShouldBeTrue();
        plan.Plan!.OrderedNodes[0].Type.ShouldBe("translator");
    }

    [Fact]
    public void InvalidConstraint_ShouldFailCompile()
    {
        var raw = """
dsl_version: "0.1"
goal:
  name: "invalid-constraint"
strategy: "cot"
budget:
  max_steps: 2
  token_limit: 200
nodes:
  - id: "worker"
    type: "WorkerAgent"
edges: []
constraints:
  - type: "language"
    value: "zh"
""";

        var compiler = new PlatformMeshCompiler();
        var compile = compiler.Compile(raw);
        compile.Ok.ShouldBeFalse();
        compile.Errors.ShouldContain(e => e.Code == "constraint.unsupported_type");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aevatar_platform_core_tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
