using System.IO;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Agents.Cognitive.Utilities;

// ============================================================
//  AgentYamlResolver
//
//  Purpose:
//  - Load agent YAML config by role for Cognitive workflows
//  - Search local ./aevatar/agents first, then ~/.aevatar/agents
// ============================================================
internal static class AgentYamlResolver
{
    public static AgentYamlConfig? TryLoad(string? role, string? workingDirectory = null)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        if (key.Length == 0)
            return null;

        var loader = new AgentYamlConfigLoader();
        var baseDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory!.Trim();

        var localDir = Path.Combine(baseDir, "aevatar", "agents");
        var globalDir = AgentYamlConfigLoader.GetDefaultConfigDirectory();

        return loader.TryLoadFromFile(Path.Combine(localDir, $"{key}.yaml"))
               ?? loader.TryLoadFromFile(Path.Combine(localDir, $"{key}.yml"))
               ?? loader.TryLoadFromFile(Path.Combine(globalDir, $"{key}.yaml"))
               ?? loader.TryLoadFromFile(Path.Combine(globalDir, $"{key}.yml"));
    }
}
