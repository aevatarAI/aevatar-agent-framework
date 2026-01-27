using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Workshop;

public static class WorkshopAgentYamlBootstrap
{
    public static void EnsureDefaultAgentYaml(WorkshopOptions options)
    {
        if (options == null || !options.EnableAgentYaml)
            return;

        var role = GlobalAgentYamlRegistry.NormalizeRoleKey(options.AgentRole);
        if (string.IsNullOrWhiteSpace(role))
            return;

        try
        {
            WriteYamlIfMissing(role, BuildYaml(role));
            WriteYamlIfMissing("sisyphus", BuildSisyphusYaml());
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

    private static string BuildYaml(string role)
    {
        return $$"""
id: "{{role}}"
name: "Aevatar.Workshop"
version: "1.0"
provider: "default"
tools: []
system_prompt: |
  You are a helpful assistant. (from agent.yaml)
extensions:
  event_modules: "{{WorkshopChatTraceModule.ModuleName}}, {{WorkshopPingModule.ModuleName}}"
""";
    }

    private static string BuildSisyphusYaml()
    {
        return $$"""
id: "sisyphus"
name: "Sisyphus (Root Coordinator)"
version: "1.0"
provider: "default"
tools:
  - "publish_event"
system_prompt: |
  You are Sisyphus, the root coordinator.
  - Users always talk to you first.
  - If a child role is more suitable, delegate via publish_event.

  publish_event schema:
  - event_type: "workshop.role.chat"
  - payload:
      target_role: "<role_id>"
      message: "<task message>"
      request_id: "<optional request id>"
      stream_chunk_every_n: 4

  Always keep responses concise and actionable.
extensions:
  event_modules: "{{WorkshopGroupAgUiModule.ModuleName}}, {{WorkshopGroupTaskModule.ModuleName}}, {{WorkshopChatTraceModule.ModuleName}}"
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
  - Always include event modules: workshop_group_agui, workshop_group_task, workshop_chat_trace.
  - Use file_read to inspect existing YAML before writing.

  Required YAML fields:
  - id, name, provider, tools, system_prompt, extensions.event_modules

  Output only the YAML content when writing files.
extensions:
  event_modules: "{{WorkshopGroupAgUiModule.ModuleName}}, {{WorkshopGroupTaskModule.ModuleName}}, {{WorkshopChatTraceModule.ModuleName}}"
""";
    }
}
