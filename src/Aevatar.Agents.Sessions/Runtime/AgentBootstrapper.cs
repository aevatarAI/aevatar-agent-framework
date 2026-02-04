using System.Collections.Concurrent;
using System.Diagnostics;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Runtime;

// ============================================================
//  AgentBootstrapper - 极简版
//
//  职责单一：
//  - Agent 默认配置
//  - LLM provider 初始化
//
//  删除的冗余：
//  - SessionAgUiStream 依赖（状态事件）
//  - 复杂的初始化进度通知
// ============================================================

public sealed class AgentBootstrapper
{
    private readonly IOptionsMonitor<SessionRuntimeOptions> _options;
    private readonly IOptionsMonitor<LLMProvidersConfig> _llmProviders;
    private readonly RoleAgentFactory _roleAgentFactory;
    private readonly ILogger<AgentBootstrapper> _logger;
    private readonly ConcurrentDictionary<string, bool> _configuredAgents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _initializedAgents = new(StringComparer.Ordinal);

    public AgentBootstrapper(
        IOptionsMonitor<SessionRuntimeOptions> options,
        IOptionsMonitor<LLMProvidersConfig> llmProviders,
        RoleAgentFactory roleAgentFactory,
        ILogger<AgentBootstrapper> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _llmProviders = llmProviders ?? throw new ArgumentNullException(nameof(llmProviders));
        _roleAgentFactory = roleAgentFactory ?? throw new ArgumentNullException(nameof(roleAgentFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ConfigureAgentDefaults(string agentId, IAIGAgent agent, bool memoryEnabled)
    {
        if (!_configuredAgents.TryAdd(agentId, true))
            return;

        var options = _options.CurrentValue ?? new SessionRuntimeOptions();
        agent.EnableChatHistoryInState = true;
        agent.EnableChatHistoryCompaction = false;
        agent.ChatHistoryMaxMessages = Math.Max(1, options.MaxSnapshotMessages);
        agent.EnableMemoryStoreAppend = memoryEnabled;
        agent.EnableSessionMemoryStoreAppend = memoryEnabled;

        if (string.IsNullOrWhiteSpace(agent.SystemPrompt))
            agent.SystemPrompt = options.SystemPrompt ?? string.Empty;
    }

    public async Task EnsureInitializedAsync(
        SessionAgUiStream stream,
        IAIGAgent agent,
        string requestId,
        string? role,
        CancellationToken ct)
    {
        if (_initializedAgents.ContainsKey(agent.Id))
            return;

        var providerName = ResolveProviderName(_llmProviders.CurrentValue);
        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException("LLMProviders.default 未配置");

        _logger.LogDebug("[AgentBootstrapper] Initializing agent {AgentId} with provider {Provider}", agent.Id, providerName);

        var options = _options.CurrentValue ?? new SessionRuntimeOptions();
        var yamlConfig = BuildYamlConfigAction(options, role);

        var sw = Stopwatch.StartNew();
        await agent.InitializeAsync(
            providerName,
            config =>
            {
                config.Temperature = (float)options.Temperature;
                config.MaxOutputTokens = options.MaxOutputTokens;
                yamlConfig?.Invoke(config);
            },
            ct);

        _initializedAgents.TryAdd(agent.Id, true);

        sw.Stop();
        _logger.LogInformation("[AgentBootstrapper] Agent {AgentId} initialized in {Elapsed:F1}s", agent.Id, sw.Elapsed.TotalSeconds);
    }

    public async Task TryConfigureRoleAgentAsync(IGAgentActor actor, string? role, CancellationToken ct)
    {
        try
        {
            if (actor.GetAgent() is RoleAIGAgent agent)
            {
                agent.InitializeRole(role);
                if (!string.IsNullOrWhiteSpace(role))
                    await _roleAgentFactory.ApplyYamlAsync(agent, role, ct);
            }
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Role agent configuration requires local runtime for {AgentId}", actor.Id);
        }
    }

    private static string ResolveProviderName(LLMProvidersConfig config)
    {
        var fallback = config.Providers.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;

        return string.IsNullOrWhiteSpace(config.Default)
            ? fallback.Trim()
            : config.Default.Trim();
    }

    private Action<AevatarAIAgentConfig>? BuildYamlConfigAction(SessionRuntimeOptions options, string? role)
    {
        if (!options.EnableAgentYaml)
            return null;

        var key = (role ?? string.Empty).Trim();
        if (key.Length == 0)
            return null;

        return _roleAgentFactory.BuildYamlConfigAction(key);
    }
}
