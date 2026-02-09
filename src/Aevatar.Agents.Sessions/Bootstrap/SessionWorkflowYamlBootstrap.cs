using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Sessions.Runtime;

namespace Aevatar.Agents.Sessions.Bootstrap;

public static class SessionWorkflowYamlBootstrap
{
    public static void EnsureDefaultWorkflowYaml(SessionRuntimeOptions options)
    {
        if (options == null || !options.EnableWorkflowYaml)
            return;

        var workflowName = (options.WorkflowName ?? string.Empty).Trim();
        if (workflowName.Length == 0)
            return;

        try
        {
            var dir = GetWorkflowDirectory();
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{workflowName}.yaml");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, BuildYaml(options));
        }
        catch
        {
            // Best-effort only; demo should still run without workflow bootstrap.
        }
    }

    public static string GetWorkflowDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar", "workflows");
    }

    public static string GetWorkflowPath(string workflowName)
    {
        var name = (workflowName ?? string.Empty).Trim();
        if (name.Length == 0)
            name = "workspace_mesh";
        return Path.Combine(GetWorkflowDirectory(), $"{name}.yaml");
    }

    private static string BuildYaml(SessionRuntimeOptions options)
    {
        var role = GlobalAgentYamlRegistry.NormalizeRoleKey(options.AgentRole);
        if (string.IsNullOrWhiteSpace(role))
            role = "sisyphus";

        return $$"""
dsl_version: "0.1"
goal:
  name: "Workspace Demo Workflow"
  success_metric: "balanced plan with critiques"
strategy: cot
budget:
  max_steps: 6
  token_limit: 4000
nodes:
  - id: planner
    type: "{{role}}"
  - id: critic
    type: "{{role}}"
  - id: synthesizer
    type: "{{role}}"
  - id: verifier
    type: "{{role}}"
edges:
  - from: planner
    to: critic
    channel: question
  - from: critic
    to: synthesizer
    channel: upstream_output
  - from: synthesizer
    to: verifier
    channel: upstream_output
constraints:
  - type: max_iterations
    value: 3
""";
    }
}
