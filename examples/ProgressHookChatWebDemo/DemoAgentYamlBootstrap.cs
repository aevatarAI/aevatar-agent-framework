using Aevatar.Agents.AI.Core.Configuration;

namespace ProgressHookChatWebDemo;

public static class DemoAgentYamlBootstrap
{
    public static void EnsureDemoAgentYaml(DemoOptions options)
    {
        if (options == null)
            return;

        if (!options.EnableAgentYaml)
            return;

        var role = GlobalAgentYamlRegistry.NormalizeRoleKey(options.AgentRole);
        if (string.IsNullOrWhiteSpace(role))
            return;

        try
        {
            var dir = AgentYamlConfigLoader.GetDefaultConfigDirectory();
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{role}.yaml");
            if (File.Exists(path))
                return;

            File.WriteAllText(path, BuildYaml(role));
        }
        catch
        {
            // Best-effort only; demo should still run without YAML bootstrap.
        }
    }

    private static string BuildYaml(string role)
    {
        return $$"""
id: "{{role}}"
name: "ProgressHookChatWebDemo"
version: "1.0"
system_prompt: |
  You are a helpful assistant. (from agent.yaml)
extensions:
  event_modules: "{{DemoChatTraceModule.ModuleName}}"
  event_routes: |
    - when: event.type == "aevatar.agents.ai.core.ChatRequestEvent"
      to: {{DemoChatTraceModule.ModuleName}}
    - when: event.type == "aevatar.agents.ai.core.ChatResponseEvent"
      to: {{DemoChatTraceModule.ModuleName}}
""";
    }
}
