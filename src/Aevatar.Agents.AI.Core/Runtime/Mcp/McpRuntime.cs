using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
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
    private readonly IMcpRuntimeHost _host;
    private readonly IMcpClientFactory _clientFactory;
    private readonly IMcpClock _clock;

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Dictionary<string, IMCPClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastAttemptUtc = DateTimeOffset.MinValue;

    internal McpRuntime(IMcpRuntimeHost host, IMcpClientFactory clientFactory, IMcpClock clock)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal async Task<bool> RegisterFromConfigurationBestEffortAsync(
        bool isRetry,
        CancellationToken cancellationToken = default)
    {
        if (!_host.EnableMcpServers)
            return false;

        var cfg = _host.HostConfiguration;
        if (cfg == null)
            return false;

        var resolved = MCPServersConfigReader.Resolve(cfg);
        if (!resolved.AutoConnect || resolved.Servers.Count == 0)
            return false;

        // On retries, avoid spamming connection attempts.
        if (isRetry && _host.McpRetryOnEachChat)
        {
            // Effective min-interval: config (MCP:retryMinIntervalSeconds) overrides code defaults.
            var effectiveMinInterval = _host.McpRetryMinInterval;
            if (resolved.RetryMinIntervalSeconds.HasValue)
            {
                var seconds = Math.Clamp(resolved.RetryMinIntervalSeconds.Value, 0, 3600);
                effectiveMinInterval = TimeSpan.FromSeconds(seconds);
            }

            var now = _clock.UtcNow;
            if (_lastAttemptUtc != DateTimeOffset.MinValue &&
                now - _lastAttemptUtc < effectiveMinInterval)
            {
                return false;
            }
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            _lastAttemptUtc = _clock.UtcNow;

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
                    _host.Logger.LogInformation("[MCP] Connecting server '{Key}' ({Transport}) from {Source}...",
                        s.Key, s.Config.TransportType, resolved.Source);

                    var client = await _clientFactory.CreateAsync(s.Config, _host.Logger, cancellationToken);

                    await _host.RegisterMcpToolsAsync(s.Key, client, s.ToolNamePrefix, s.Config, cancellationToken);

                    _clients[s.Key] = client;
                    ok++;
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    _host.Logger.LogWarning(ex,
                        "[MCP] Failed to connect/register server '{Key}' (best-effort).", s.Key);
                }
            }

            if (ok > 0 || failed > 0)
            {
                _host.Logger.LogInformation("[MCP] Auto-connect finished. ok={Ok} failed={Failed} source={Source}",
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
        if (!_host.EnableMcpServers || !_host.McpRetryOnEachChat)
            return;

        var cfg = _host.HostConfiguration;
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
            await _host.RefreshToolCachesAsync(ct);
        }
    }

    internal async Task<(bool Ok, string? Error)> ReconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await _host.InitializeToolsAsync(ct);
            var registered = await RegisterFromConfigurationBestEffortAsync(isRetry: false, ct);
            await _host.RefreshToolCachesAsync(ct);
            return (registered || true, null);
        }
        catch (Exception ex)
        {
            _host.Logger.LogWarning(ex, "MCP reconnect failed: {Message}", ex.Message);
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
                _host.Logger,
                LogLevel.Debug,
                "[MCP] Failed to dispose MCP client (best-effort).");
        }

        _clients.Clear();
    }
}

