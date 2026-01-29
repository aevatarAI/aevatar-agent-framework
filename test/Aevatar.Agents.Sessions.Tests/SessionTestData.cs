namespace Aevatar.Agents.Sessions.Tests;

internal sealed record WorkflowSpec(
    string WorkflowName,
    string WorkflowPath,
    string NodeId,
    string Role);

internal static class SessionTestData
{
    public static WorkflowSpec WriteWorkflow(string dir, string? role = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workflowName = $"session_test_{suffix}";
        var nodeId = $"node_{suffix}";
        var nodeRole = string.IsNullOrWhiteSpace(role) ? "reviewer" : role.Trim();
        var path = Path.Combine(dir, $"{workflowName}.yaml");

        var yaml = $"""
dsl_version: "0.1"
goal:
  name: "Session Test"
  success_metric: "ok"
strategy: cot
budget:
  max_steps: 1
  token_limit: 64
nodes:
  - id: {nodeId}
    type: {nodeRole}
edges: []
constraints: []
""";

        File.WriteAllText(path, yaml);
        return new WorkflowSpec(workflowName, Path.GetFullPath(path), nodeId, nodeRole);
    }
}
