using Aevatar.Agents.AI.Core;

namespace Aevatar.Platform.Core.Agents;

// ============================================================
//  RoleAIGAgent
//
//  说明：
//  - 通用 role 驱动 Agent，承载大多数平台角色（coder/reviewer/...）。
//  - 角色配置由 RoleAgentFactory 通过 AgentYamlConfigApplier 注入。
// ============================================================
public sealed class RoleAIGAgent : AIGAgentBase
{
    public string Role { get; private set; } = "role";

    public void InitializeRole(string? role)
    {
        var t = (role ?? string.Empty).Trim();
        Role = t.Length == 0 ? "role" : t;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"RoleAIGAgent({Role})");
}


