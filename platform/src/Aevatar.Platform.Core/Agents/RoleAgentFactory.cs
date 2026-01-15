using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Platform.Core.Agents;

// ============================================================
//  RoleAgentFactory
//
//  说明：
//  - 统一创建 RoleAIGAgent，并应用 ~/.aevatar/agents/{role}.yaml
//  - skills/tools allowlist 由 AgentYamlConfigApplier 负责收敛
// ============================================================
public sealed class RoleAgentFactory
{
    private readonly GlobalAgentYamlRegistry _registry;

    public RoleAgentFactory(GlobalAgentYamlRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<RoleAIGAgent> CreateAsync(string? role, CancellationToken ct = default)
    {
        var agent = new RoleAIGAgent();
        agent.InitializeRole(role);

        var yaml = _registry.TryLoad(role);
        await AgentYamlConfigApplier.ApplyAsync(agent, yaml, role, ct);

        return agent;
    }
}


