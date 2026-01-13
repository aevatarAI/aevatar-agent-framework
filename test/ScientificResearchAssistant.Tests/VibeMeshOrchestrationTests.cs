using Microsoft.Extensions.Logging.Abstractions;
using ScientificResearchAssistant.Api.Vibe.Mesh;
using Shouldly;

namespace ScientificResearchAssistant.Tests;

public sealed class VibeMeshOrchestrationTests
{
    // ------------------------------------------------------------
    //  MeshCompilerService
    // ------------------------------------------------------------

    [Fact]
    public void MeshCompilerService_Compile_ValidSraMesh_ShouldSucceed()
    {
        var svc = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);

        var res = svc.Compile(ValidSraMeshJson);

        res.Ok.ShouldBeTrue();
        res.Definition.ShouldNotBeNull();
        res.Errors.Count.ShouldBe(0);

        res.Definition!.Nodes.Count.ShouldBe(2);
        res.Definition!.Nodes[0].Type.ShouldBe("planner");
    }

    [Fact]
    public void MeshCompilerService_Compile_ValidSraMeshYaml_ShouldSucceed()
    {
        var svc = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);

        var res = svc.Compile(ValidSraMeshYaml);

        res.Ok.ShouldBeTrue();
        res.Definition.ShouldNotBeNull();
        res.Definition!.Nodes.Count.ShouldBe(2);
        res.Definition!.Nodes[0].Type.ShouldBe("planner");
    }

    [Fact]
    public void MeshCompilerService_Compile_UnsupportedNodeType_ShouldReturnStructuredError()
    {
        var svc = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);

        var res = svc.Compile(UnsupportedNodeTypeJson);

        res.Ok.ShouldBeFalse();
        res.Definition.ShouldBeNull();
        res.Errors.Any(e => e.Code == "node.unsupported_type").ShouldBeTrue();
    }

    // ------------------------------------------------------------
    //  MeshExecutionPlanner
    // ------------------------------------------------------------

    [Fact]
    public void MeshExecutionPlanner_Plan_ShouldRejectUnsupportedChannel()
    {
        var compiler = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);
        var planner = new MeshExecutionPlanner();

        var compiled = compiler.Compile(UnsupportedChannelJson);
        compiled.Ok.ShouldBeTrue();
        compiled.Definition.ShouldNotBeNull();

        var planRes = planner.Plan("test001", "run001", compiled.Definition!);
        planRes.Ok.ShouldBeFalse();
        planRes.Plan.ShouldBeNull();
        planRes.Errors.Any(e => e.Code == "edge.unsupported_channel").ShouldBeTrue();
    }

    [Fact]
    public void MeshExecutionPlanner_Plan_ShouldReturnDeterministicTopoOrder()
    {
        var compiler = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);
        var planner = new MeshExecutionPlanner();

        var compiled = compiler.Compile(DeterministicTopoJson);
        compiled.Ok.ShouldBeTrue();
        compiled.Definition.ShouldNotBeNull();

        var planRes = planner.Plan("test001", "run001", compiled.Definition!);
        planRes.Ok.ShouldBeTrue();
        planRes.Plan.ShouldNotBeNull();

        // ready-set tie break uses ordinal id ordering => after 'a' both 'b' and 'c' become ready.
        planRes.Plan!.TopoOrder.ToList().ShouldBe(new List<string> { "a", "b", "c" });
    }

    [Fact]
    public void MeshExecutionPlanner_Plan_Cycle_ShouldReturnActionableError()
    {
        var compiler = new MeshCompilerService(NullLogger<MeshCompilerService>.Instance);
        var planner = new MeshExecutionPlanner();

        var compiled = compiler.Compile(CycleJson);
        compiled.Ok.ShouldBeTrue();
        compiled.Definition.ShouldNotBeNull();

        var planRes = planner.Plan("test001", "run001", compiled.Definition!);
        planRes.Ok.ShouldBeFalse();
        planRes.Plan.ShouldBeNull();
        planRes.Errors.Any(e => e.Code == "mesh.cycle_detected").ShouldBeTrue();
    }

    // ------------------------------------------------------------
    //  Test payloads
    // ------------------------------------------------------------

    private const string ValidSraMeshJson = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "SRA mesh smoke test" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 5000 },
      "nodes": [
        { "id": "planner", "type": "planner" },
        { "id": "reasoner", "type": "reasoner" }
      ],
      "edges": [
        { "from": "planner", "to": "reasoner", "channel": "upstream_output" }
      ],
      "constraints": [
        { "type": "max_iterations", "value": 3 }
      ]
    }
    """;

    private const string ValidSraMeshYaml = """
    dsl_version: "0.1"
    goal: { name: "SRA mesh yaml smoke test" }
    strategy: "cot"
    budget: { max_steps: 10, token_limit: 5000 }
    nodes:
      - { id: planner, type: planner }
      - { id: reasoner, type: reasoner }
    edges:
      - { from: planner, to: reasoner, channel: upstream_output }
    constraints:
      - { type: max_iterations, value: 3 }
    """;

    private const string UnsupportedNodeTypeJson = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "bad node type" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 5000 },
      "nodes": [
        { "id": "x", "type": "not_supported" }
      ],
      "edges": [],
      "constraints": []
    }
    """;

    private const string UnsupportedChannelJson = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "bad channel" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 5000 },
      "nodes": [
        { "id": "planner", "type": "planner" },
        { "id": "reasoner", "type": "reasoner" }
      ],
      "edges": [
        { "from": "planner", "to": "reasoner", "channel": "not_supported_channel" }
      ],
      "constraints": []
    }
    """;

    private const string DeterministicTopoJson = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "topo order" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 5000 },
      "nodes": [
        { "id": "a", "type": "planner" },
        { "id": "b", "type": "reasoner" },
        { "id": "c", "type": "librarian" }
      ],
      "edges": [
        { "from": "a", "to": "b", "channel": "upstream_output" },
        { "from": "a", "to": "c", "channel": "upstream_output" }
      ],
      "constraints": []
    }
    """;

    private const string CycleJson = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "cycle" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 5000 },
      "nodes": [
        { "id": "a", "type": "planner" },
        { "id": "b", "type": "reasoner" }
      ],
      "edges": [
        { "from": "a", "to": "b", "channel": "upstream_output" },
        { "from": "b", "to": "a", "channel": "upstream_output" }
      ],
      "constraints": []
    }
    """;
}


