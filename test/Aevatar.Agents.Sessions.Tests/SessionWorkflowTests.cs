using System.ComponentModel;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Sessions.Tests;

public class SessionWorkflowTests(SessionsTestFixture fixture) : IClassFixture<SessionsTestFixture>
{
    private readonly SessionsTestFixture _fixture = fixture;

    [Fact]
    [DisplayName("StartSessionAsync should load workflow YAML and resolve roles")]
    public async Task StartSessionAsync_ShouldLoadWorkflowYaml_AndResolveRoles()
    {
        var spec = WorkflowTestData.WriteSampleWorkflow(_fixture.WorkflowsDirectory);
        var state = await _fixture.Sessions.StartSessionAsync(new StartSessionRequest
        {
            WorkflowName = spec.WorkflowName
        });

        state.WorkflowPath.ShouldBe(spec.WorkflowPath);
        state.Roles.Count.ShouldBe(2);

        var alpha = state.Roles.Single(r => r.NodeId == spec.AlphaNodeId);
        alpha.Role.ShouldBe(spec.AlphaRole);
        alpha.AgentId.ShouldBe(BuildExpectedAgentId(state.SessionId, spec.AlphaNodeId));
        alpha.Loaded.ShouldBeFalse();

        var beta = state.Roles.Single(r => r.NodeId == spec.BetaNodeId);
        beta.Role.ShouldBe(spec.BetaRole);
        beta.AgentId.ShouldBe(BuildExpectedAgentId(state.SessionId, spec.BetaNodeId));
        beta.Loaded.ShouldBeFalse();
    }

    [Fact]
    [DisplayName("Session workflow should run via role agent after lazy load")]
    public async Task StartSessionAsync_ShouldRunRoleAgent_WhenLazyLoaded()
    {
        var spec = WorkflowTestData.WriteSampleWorkflow(_fixture.WorkflowsDirectory);
        var state = await _fixture.Sessions.StartSessionAsync(new StartSessionRequest
        {
            WorkflowName = spec.WorkflowName
        });

        var bundle = await _fixture.Sessions.GetSessionAgentStatesAsync(
            state.SessionId,
            includeHistory: false,
            historyLimit: 1);
        bundle.ShouldNotBeNull();

        var refreshed = await _fixture.Sessions.GetSessionStateAsync(state.SessionId);
        refreshed.ShouldNotBeNull();
        refreshed!.Roles.All(r => r.Loaded).ShouldBeTrue();

        var alpha = refreshed.Roles.Single(r => r.NodeId == spec.AlphaNodeId);
        var actor = await _fixture.ActorManager.GetActorAsync(alpha.AgentId);
        actor.ShouldNotBeNull();

        var agent = actor!.GetAgent() as RoleAIGAgent;
        agent.ShouldNotBeNull();

        await agent!.InitializeAsync("test-provider");
        var response = await agent.ChatAsync(ChatRequest.Create("ping"));
        response.Content.ShouldBe("pong");
    }

    [Fact]
    [DisplayName("Same workflow should generate session-scoped agent ids")]
    public async Task StartSessionAsync_ShouldGenerateSessionScopedAgentIds()
    {
        var spec = WorkflowTestData.WriteSampleWorkflow(_fixture.WorkflowsDirectory);

        var first = await _fixture.Sessions.StartSessionAsync(new StartSessionRequest
        {
            WorkflowName = spec.WorkflowName
        });

        var second = await _fixture.Sessions.StartSessionAsync(new StartSessionRequest
        {
            WorkflowName = spec.WorkflowName
        });

        var firstAlpha = first.Roles.Single(r => r.NodeId == spec.AlphaNodeId);
        var secondAlpha = second.Roles.Single(r => r.NodeId == spec.AlphaNodeId);

        firstAlpha.AgentId.ShouldNotBe(secondAlpha.AgentId);
        firstAlpha.AgentId.ShouldBe(BuildExpectedAgentId(first.SessionId, spec.AlphaNodeId));
        secondAlpha.AgentId.ShouldBe(BuildExpectedAgentId(second.SessionId, spec.AlphaNodeId));
    }

    private sealed record WorkflowSpec(
        string WorkflowName,
        string WorkflowPath,
        string AlphaNodeId,
        string BetaNodeId,
        string AlphaRole,
        string BetaRole);

    private static string BuildExpectedAgentId(string sessionId, string nodeId)
    {
        var safeSession = Sanitize(sessionId);
        var safeNode = Sanitize(nodeId);
        if (safeSession.Length == 0) safeSession = "session";
        if (safeNode.Length == 0) safeNode = "node";
        return AgentId.Normalize<RoleAIGAgent>($"{safeSession}__{safeNode}");
    }

    private static string Sanitize(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Replace(AgentId.Separator, '_');
    }

    private static class WorkflowTestData
    {
        public static WorkflowSpec WriteSampleWorkflow(string dir)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var workflowName = $"session_test_{suffix}";
            var alpha = $"alpha_{suffix}";
            var beta = $"beta_{suffix}";
            var path = Path.Combine(dir, $"{workflowName}.yaml");

            var yaml = $"""
dsl_version: "0.1"
goal:
  name: "Session Test"
  success_metric: "ok"
strategy: cot
budget:
  max_steps: 1
  token_limit: 128
nodes:
  - id: {alpha}
    type: reviewer
  - id: {beta}
    type: worker
    params:
      role: writer
edges: []
constraints: []
""";

            File.WriteAllText(path, yaml);
            return new WorkflowSpec(
                WorkflowName: workflowName,
                WorkflowPath: Path.GetFullPath(path),
                AlphaNodeId: alpha,
                BetaNodeId: beta,
                AlphaRole: "reviewer",
                BetaRole: "writer");
        }
    }
}
