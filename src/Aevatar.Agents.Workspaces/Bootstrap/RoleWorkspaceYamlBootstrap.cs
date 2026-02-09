using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Workspaces.Core;
using Aevatar.Agents.Workspaces.Events;

namespace Aevatar.Agents.Workspaces.Bootstrap;

public static class RoleWorkspaceYamlBootstrap
{
    public static void EnsureDefaultRoleYaml(RoleWorkspaceOptions options)
    {
        if (options == null || !options.EnableAgentYaml)
            return;

        var root = GlobalAgentYamlRegistry.NormalizeRoleKey(options.RootRole);
        if (string.IsNullOrWhiteSpace(root))
            root = "sisyphus";

        try
        {
            WriteYamlIfMissing(root, BuildRootYaml(root));
            WriteYamlIfMissing("hermes", BuildHermesYaml());
        }
        catch
        {
            // Best-effort only; demo should still run without YAML bootstrap.
        }
    }

    private static void WriteYamlIfMissing(string role, string yaml)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        if (string.IsNullOrWhiteSpace(key))
            return;

        var dir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"{key}.yaml");
        if (File.Exists(path))
            return;

        File.WriteAllText(path, yaml);
    }

    private static string BuildRootYaml(string role)
    {
        return $$"""
id: "{{role}}"
name: "Workspace Root"
version: "1.0"
provider: "default"
tools:
  - "publish_event"
system_prompt: |
  You are the root coordinator.
  - Users always talk to you first.
  - If a child role is more suitable, delegate via publish_event.

  publish_event schema:
  - event_type: "workspace.role.chat"
  - payload:
      target_role: "<role_id>"
      message: "<task message>"
      request_id: "<optional request id>"
      stream_chunk_every_n: 4

  Always keep responses concise and actionable.
extensions:
  event_modules: "{{RoleWorkspaceGroupAgUiModule.ModuleName}}, {{RoleWorkspaceGroupTaskModule.ModuleName}}, {{RoleWorkspaceChatTraceModule.ModuleName}}, {{RoleWorkspacePingModule.ModuleName}}"
""";
    }

    private static string BuildHermesYaml()
    {
        return $$"""
id: "hermes"
name: "Hermes (Role Creator)"
version: "1.0"
provider: "default"
tools:
  - "file_read"
  - "file_write"
system_prompt: |
  You are Hermes, a role creator.
  - You create new role YAML files under ~/.aevatar/agents.
  - Always include event modules: workspace_group_agui, workspace_group_task, workspace_chat_trace.
  - Use file_read to inspect existing YAML before writing.

  Required YAML fields:
  - id, name, provider, tools, system_prompt, extensions.event_modules

  Output only the YAML content when writing files.
extensions:
  event_modules: "{{RoleWorkspaceGroupAgUiModule.ModuleName}}, {{RoleWorkspaceGroupTaskModule.ModuleName}}, {{RoleWorkspaceChatTraceModule.ModuleName}}, {{RoleWorkspacePingModule.ModuleName}}"
""";
    }
}
