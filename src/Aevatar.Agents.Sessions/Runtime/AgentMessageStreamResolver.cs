using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Sessions.Runtime;

public interface IAgentMessageStreamResolver
{
    IMessageStream GetStream(string agentId);
}

public sealed class AgentMessageStreamResolver : IAgentMessageStreamResolver
{
    // ============================================================
    // 中文 + ASCII:
    // - 统一解析 Agent 的消息流
    // - 优先外部 provider，否则回退 Local registry
    // ============================================================

    private readonly IMessageStreamProvider? _provider;
    private readonly LocalMessageStreamRegistry? _localRegistry;
    private readonly ILogger<AgentMessageStreamResolver>? _logger;

    public AgentMessageStreamResolver(
        LocalMessageStreamRegistry localRegistry,
        ILogger<AgentMessageStreamResolver>? logger = null,
        IMessageStreamProvider? provider = null)
    {
        // localRegistry is now REQUIRED to ensure subscription works with Local runtime.
        _localRegistry = localRegistry ?? throw new ArgumentNullException(nameof(localRegistry));
        _provider = provider;
        _logger = logger;

        _logger?.LogDebug("[AgentMessageStreamResolver] Initialized with LocalRegistry={HasRegistry}, Provider={HasProvider}",
            _localRegistry != null, _provider != null);
    }

    public IMessageStream GetStream(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("agentId is required.", nameof(agentId));

        if (_provider != null)
        {
            _logger?.LogDebug("[AgentMessageStreamResolver] Using external provider for agent {AgentId}", agentId);
            return _provider.GetStream(agentId, null);
        }

        _logger?.LogDebug("[AgentMessageStreamResolver] Using local registry for agent {AgentId}", agentId);
        return _localRegistry!.GetOrCreateStream(agentId);
    }
}
