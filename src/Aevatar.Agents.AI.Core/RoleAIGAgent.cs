namespace Aevatar.Agents.AI.Core;

// ============================================================
//  RoleAIGAgent
//
//  说明：
//  - 通用 role 驱动 Agent，承载大多数角色（coder/reviewer/...）。
//  - 行为由 YAML + 工具策略装配，类本身保持极简。
// ============================================================
public sealed class RoleAIGAgent : AIGAgentBase
{
    public string Role { get; private set; } = "role";

    public void InitializeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim();
        Role = value.Length == 0 ? "role" : value;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"RoleAIGAgent({Role})");
}

