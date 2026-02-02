using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Microsoft.Extensions.Logging;

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
    private readonly IEventModuleFactory[] _moduleFactories;
    private readonly IEventRouteEvaluator _routeEvaluator;
    private readonly ILogger<RoleAgentFactory>? _logger;

    public RoleAgentFactory(
        IGAgentFactory agentFactory,
        GlobalAgentYamlRegistry registry,
        IEnumerable<IEventModuleFactory>? moduleFactories = null,
        IEventRouteEvaluator? routeEvaluator = null,
        ILogger<RoleAgentFactory>? logger = null)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _moduleFactories = moduleFactories?.ToArray() ?? Array.Empty<IEventModuleFactory>();
        _routeEvaluator = routeEvaluator ?? new DefaultEventRouteEvaluator();
        _logger = logger;
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
            ApplyEventModulesFromYaml(agent, yaml);
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

    public async Task ApplyYamlAsync<TState>(RoleAIGAgent<TState> agent, string? role, CancellationToken ct = default)
        where TState : class, Google.Protobuf.IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(agent);
        var yaml = _registry.TryLoad(role);
        await AgentYamlConfigApplier.ApplyAsync(agent, yaml, role, ct);
        ApplyEventModulesFromYaml(agent, yaml);
    }

    private void ApplyEventModulesFromYaml<TState>(RoleAIGAgent<TState> agent, AgentYamlConfig? yaml)
        where TState : class, Google.Protobuf.IMessage<TState>, new()
    {
        if (agent == null || yaml == null || yaml.Extensions == null)
            return;

        if (!TryGetExtension(yaml.Extensions, "event_modules", out var moduleRaw))
            return;

        var moduleNames = SplitCsv(moduleRaw);
        if (moduleNames.Length == 0)
            return;

        TryGetExtension(yaml.Extensions, "event_routes", out var routesRaw);
        var routes = EventRoute.Parse(routesRaw, _logger);

        var modules = new List<IEventModule>();
        foreach (var name in moduleNames)
        {
            if (!TryCreateModule(name, out var module))
            {
                _logger?.LogWarning("[RoleAgentFactory] Unknown module '{Module}'", name);
                continue;
            }

            if (routes.Length > 0 && module is not IRouteBypassModule)
            {
                modules.Add(new RoutedEventModule(module, routes, _routeEvaluator));
            }
            else
            {
                modules.Add(module);
            }
        }

        if (modules.Count > 0)
        {
            agent.SetEventModules(modules);
        }
    }

    private bool TryCreateModule(string name, out IEventModule module)
    {
        foreach (var factory in _moduleFactories)
        {
            if (factory.TryCreate(name, out module))
                return true;
        }

        module = null!;
        return false;
    }

    private static string[] SplitCsv(string raw)
    {
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryGetExtension(
        IReadOnlyDictionary<string, object> extensions,
        string key,
        out string value)
    {
        foreach (var (k, v) in extensions)
        {
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                continue;
            value = v?.ToString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

