using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Agents.AI.Core;

// ============================================================
//  RoleAgentFactory
//
//  说明：
//  - 框架层 role 装配入口：创建 RoleAIGAgent 并应用 role YAML。
//  - 通过 IGAgentFactory 创建实例，确保依赖注入一致。
//  - 模型参数建议在 InitializeAsync 配置阶段或之后再次应用（避免被 ConfigAI 覆盖）。
// ============================================================
public sealed class RoleAgentFactory
{
    private readonly IGAgentFactory _agentFactory;
    private readonly GlobalAgentYamlRegistry _registry;

    public RoleAgentFactory(IGAgentFactory agentFactory, GlobalAgentYamlRegistry registry)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<RoleAIGAgent> CreateAsync(string? role, CancellationToken ct = default)
    {
        var agent = _agentFactory.CreateGAgent<RoleAIGAgent>(ct);
        agent.InitializeRole(role);

        var yaml = _registry.TryLoad(role);
        if (yaml != null)
        {
            // 中文 + ASCII:
            // - 初始化前仅注入 prompt + tools/skills，模型参数留给 InitializeAsync 配置期或 ApplyYamlAsync。
            AgentYamlConfigApplier.ApplySystemPrompt(agent, yaml, role);
            await AgentYamlConfigApplier.ApplyToolsAndSkillsAsync(agent, yaml, ct);
        }

        return agent;
    }

    public Action<AevatarAIAgentConfig> BuildYamlConfigAction(string? role)
    {
        var yaml = _registry.TryLoad(role);
        return cfg =>
        {
            if (yaml is null) return;
            AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);
        };
    }

    public async Task ApplyYamlAsync(RoleAIGAgent agent, string? role, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var yaml = _registry.TryLoad(role);
        await AgentYamlConfigApplier.ApplyAsync(agent, yaml, role, ct);
    }
}

