using Aevatar.Agents.AI.Tool.MCP;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// MCP auto-connection runtime extracted from <see cref="AIGAgentBase"/>.
///
/// 中文 + ASCII:
/// - 负责：从 IConfiguration 解析 mcpServers、连接/重连、注册工具、生命周期 Dispose
/// - 失败语义：best-effort（返回 false/忽略），不影响 Chat 主路径
/// </summary>
internal sealed class McpRuntime
{
    private readonly AIGAgentBase _owner;

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Dictionary<string, IAsyncDisposable> _clients = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastAttemptUtc = DateTimeOffset.MinValue;

    internal McpRuntime(AIGAgentBase owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    internal async Task<bool> RegisterFromConfigurationBestEffortAsync(
        bool isRetry,
        CancellationToken cancellationToken = default)
    {
        if (!_owner.EnableMcpServers)
            return false;

        var cfg = _owner.InternalHostConfiguration;
        if (cfg == null)
            return false;

        var resolved = MCPServersConfigReader.Resolve(cfg);
        if (!resolved.AutoConnect || resolved.Servers.Count == 0)
            return false;

        // On retries, avoid spamming connection attempts.
        if (isRetry && _owner.McpRetryOnEachChat)
        {
            // Effective min-interval: config (MCP:retryMinIntervalSeconds) overrides code defaults.
            var effectiveMinInterval = _owner.McpRetryMinInterval;
            if (resolved.RetryMinIntervalSeconds.HasValue)
            {
                var seconds = Math.Clamp(resolved.RetryMinIntervalSeconds.Value, 0, 3600);
                effectiveMinInterval = TimeSpan.FromSeconds(seconds);
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastAttemptUtc != DateTimeOffset.MinValue &&
                now - _lastAttemptUtc < effectiveMinInterval)
            {
                return false;
            }
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            _lastAttemptUtc = DateTimeOffset.UtcNow;

            var ok = 0;
            var failed = 0;
            var registeredAny = false;

            foreach (var s in resolved.Servers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!s.Enabled)
                    continue;

                // Avoid duplicate connections in the same agent lifetime.
                if (_clients.ContainsKey(s.Key))
                    continue;

                try
                {
                    _owner.InternalLogger.LogInformation("[MCP] Connecting server '{Key}' ({Transport}) from {Source}...",
                        s.Key, s.Config.TransportType, resolved.Source);

                    var client = await MCPClientWrapper.CreateAsync(s.Config, _owner.InternalLogger, cancellationToken);

                    await _owner.InternalToolManager.RegisterMCPServerWithPrefixAsync(
                        serverUrl: s.Key,
                        mcpClient: client,
                        toolNamePrefix: s.ToolNamePrefix,
                        config: s.Config,
                        logger: _owner.InternalLogger,
                        cancellationToken: cancellationToken);

                    _clients[s.Key] = client;
                    ok++;
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    _owner.InternalLogger.LogWarning(ex,
                        "[MCP] Failed to connect/register server '{Key}' (best-effort).", s.Key);
                }
            }

            if (ok > 0 || failed > 0)
            {
                _owner.InternalLogger.LogInformation("[MCP] Auto-connect finished. ok={Ok} failed={Failed} source={Source}",
                    ok, failed, resolved.Source);
            }

            return registeredAny;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    internal async Task TryReconnectOnChatAsync(CancellationToken ct)
    {
        if (!_owner.EnableMcpServers || !_owner.McpRetryOnEachChat)
            return;

        var cfg = _owner.InternalHostConfiguration;
        if (cfg == null)
            return;

        var resolved = MCPServersConfigReader.Resolve(cfg);
        if (!resolved.AutoConnect || resolved.Servers.Count == 0)
            return;

        var hasMissing = resolved.Servers.Any(s => s.Enabled && !_clients.ContainsKey(s.Key));
        if (!hasMissing)
            return;

        var registered = await RegisterFromConfigurationBestEffortAsync(isRetry: true, ct);
        if (registered)
        {
            // New tools become visible to LLM only after refreshing caches.
            await _owner.InternalRefreshToolCachesAsync(ct);
        }
    }

    internal async Task<(bool Ok, string? Error)> ReconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await _owner.InternalInitializeToolsAsync(ct);
            var registered = await RegisterFromConfigurationBestEffortAsync(isRetry: false, ct);
            await _owner.InternalRefreshToolCachesAsync(ct);
            return (registered || true, null);
        }
        catch (Exception ex)
        {
            _owner.InternalLogger.LogWarning(ex, "MCP reconnect failed: {Message}", ex.Message);
            return (false, ex.Message);
        }
    }

    internal async Task DisposeBestEffortAsync()
    {
        if (_clients.Count == 0)
            return;

        foreach (var (_, client) in _clients.ToList())
        {
            await BestEffort.TryAsync(
                async () => await client.DisposeAsync(),
                _owner.InternalLogger,
                LogLevel.Debug,
                "[MCP] Failed to dispose MCP client (best-effort).");
        }

        _clients.Clear();
    }
}


