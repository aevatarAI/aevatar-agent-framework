using Aevatar.Agents.Abstractions;
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

    private readonly IMessageStreamProvider _provider = null!;
    private readonly ILogger<AgentMessageStreamResolver>? _logger;

    public AgentMessageStreamResolver(
        IMessageStreamProvider provider,
        ILogger<AgentMessageStreamResolver>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger;

        _logger?.LogDebug("[AgentMessageStreamResolver] Initialized with Provider={HasProvider}", _provider != null);
    }

    public IMessageStream GetStream(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("agentId is required.", nameof(agentId));

        _logger?.LogDebug("[AgentMessageStreamResolver] Resolving stream for agent {AgentId}", agentId);
        return _provider.GetStream(agentId, null);
    }
}
