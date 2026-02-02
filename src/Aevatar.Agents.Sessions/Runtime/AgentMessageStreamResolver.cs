using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Local;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private static int _resolveLogCount;

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

        IMessageStream stream;
        var source = "local";
        if (_provider != null)
        {
            _logger?.LogDebug("[AgentMessageStreamResolver] Using external provider for agent {AgentId}", agentId);
            stream = _provider.GetStream(agentId, null);
            source = "external";
        }
        else
        {
            _logger?.LogDebug("[AgentMessageStreamResolver] Using local registry for agent {AgentId}", agentId);
            stream = _localRegistry!.GetOrCreateStream(agentId);
        }

        if (System.Threading.Interlocked.Increment(ref _resolveLogCount) <= 5)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = string.Empty,
                    runId = string.Empty,
                    hypothesisId = "H8",
                    location = "AgentMessageStreamResolver.cs:GetStream",
                    message = "stream_resolve_source",
                    data = new
                    {
                        agentId,
                        source,
                        streamType = stream.GetType().Name,
                        streamId = stream.StreamId,
                        streamHash = RuntimeHelpers.GetHashCode(stream)
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }

        return stream;
    }
}
