using Aevatar.Agents.Sessions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Sessions.Tests;

public sealed class WorkflowMeshServiceTests(SessionsTestFixture fixture) : IClassFixture<SessionsTestFixture>
{
    private readonly SessionsTestFixture _fixture = fixture;

    [Fact]
    public async Task LoadFromYamlAsync_ShouldBuildGraphAndAgents()
    {
        var mesh = _fixture.ServiceProvider.GetRequiredService<WorkflowMeshService>();
        var yaml = BuildWorkflowYaml();

        var result = await mesh.LoadFromYamlAsync(yaml, CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.Graph.ShouldNotBeNull();
        result.Graph!.Nodes.Count.ShouldBe(2);
        result.Graph.Edges.Count.ShouldBe(0);
        result.Agents.Count.ShouldBe(2);
        result.Agents.All(a => !string.IsNullOrWhiteSpace(a.ActorId)).ShouldBeTrue();
    }

    private static string BuildWorkflowYaml()
    {
        return """
dsl_version: "0.1"
goal:
  name: "Mesh Test"
  success_metric: "ok"
strategy: cot
budget:
  max_steps: 1
  token_limit: 64
nodes:
  - id: alpha
    type: reviewer
  - id: beta
    type: writer
edges: []
constraints: []
""";
    }
}
