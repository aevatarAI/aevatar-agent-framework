using Aevatar.Agents.AI.WithTool.MCP;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Enable MCP server auto-connection (default: true).
    /// <para/>
    /// NOTE:
    /// - This is best-effort. If no MCP servers are configured, it's a no-op.
    /// - MCP servers may provide powerful side-effect tools; combine with your runtime policy if needed.
    /// </summary>
    public bool EnableMcpServers { get; set; } = true;

    /// <summary>
    /// If MCP servers are configured but connection failed, retry best-effort on every chat/stream call.
    /// Default: true.
    /// </summary>
    public bool McpRetryOnEachChat { get; set; } = true;

    /// <summary>
    /// Minimum interval between MCP reconnect attempts (avoids spamming when server is down).
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan McpRetryMinInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Host/app configuration (injected best-effort by <see cref="AIGAgentFactory"/>).
    /// Used to auto-connect MCP servers (Cursor-style <c>mcpServers</c>).
    /// </summary>
    protected IConfiguration? HostConfiguration { get; set; }

    private readonly SemaphoreSlim _mcpConnectLock = new(1, 1);
    private readonly Dictionary<string, IAsyncDisposable> _mcpClients =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset _mcpLastAttemptUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Auto-connect MCP servers from configuration and register their tools.
    /// <para/>
    /// Configuration shape:
    /// - Recommended: <c>MCP:mcpServers</c> (Cursor-style)
    /// - Also supported: root <c>mcpServers</c> (raw Cursor config file)
    /// </summary>
    protected virtual async Task<bool> RegisterMcpServersFromConfigurationBestEffortAsync(
        bool isRetry,
        CancellationToken cancellationToken = default)
    {
        if (!EnableMcpServers)
            return false;

        if (HostConfiguration == null)
            return false;

        var resolved = MCPServersConfigReader.Resolve(HostConfiguration);
        if (!resolved.AutoConnect || resolved.Servers.Count == 0)
            return false;

        // On retries, avoid spamming connection attempts.
        if (isRetry && McpRetryOnEachChat)
        {
            var now = DateTimeOffset.UtcNow;
            if (_mcpLastAttemptUtc != DateTimeOffset.MinValue &&
                now - _mcpLastAttemptUtc < McpRetryMinInterval)
            {
                return false;
            }
        }

        await _mcpConnectLock.WaitAsync(cancellationToken);
        try
        {
            _mcpLastAttemptUtc = DateTimeOffset.UtcNow;

            var ok = 0;
            var failed = 0;
            var registeredAny = false;

            foreach (var s in resolved.Servers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!s.Enabled)
                    continue;

                // Avoid duplicate connections in the same agent lifetime.
                if (_mcpClients.ContainsKey(s.Key))
                    continue;

                try
                {
                    Logger.LogInformation("[MCP] Connecting server '{Key}' ({Transport}) from {Source}...",
                        s.Key, s.Config.TransportType, resolved.Source);

                    var client = await MCPClientWrapper.CreateAsync(s.Config, Logger, cancellationToken);

                    await ToolManager.RegisterMCPServerWithPrefixAsync(
                        serverUrl: s.Key,
                        mcpClient: client,
                        toolNamePrefix: s.ToolNamePrefix,
                        config: s.Config,
                        logger: Logger,
                        cancellationToken: cancellationToken);

                    _mcpClients[s.Key] = client;
                    ok++;
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.LogWarning(ex,
                        "[MCP] Failed to connect/register server '{Key}' (best-effort).", s.Key);
                }
            }

            if (ok > 0 || failed > 0)
            {
                Logger.LogInformation("[MCP] Auto-connect finished. ok={Ok} failed={Failed} source={Source}",
                    ok, failed, resolved.Source);
            }

            return registeredAny;
        }
        finally
        {
            _mcpConnectLock.Release();
        }
    }

    /// <summary>
    /// Best-effort retry hook for MCP servers (used by ChatAsync/ChatStreamAsync).
    /// </summary>
    protected async Task TryReconnectMcpOnChatAsync(CancellationToken ct)
    {
        if (!EnableMcpServers || !McpRetryOnEachChat)
            return;

        // Only retry when there are configured servers that are not connected yet.
        if (HostConfiguration == null)
            return;

        var resolved = MCPServersConfigReader.Resolve(HostConfiguration);
        if (!resolved.AutoConnect || resolved.Servers.Count == 0)
            return;

        var hasMissing = resolved.Servers.Any(s => s.Enabled && !_mcpClients.ContainsKey(s.Key));
        if (!hasMissing)
            return;

        var registered = await RegisterMcpServersFromConfigurationBestEffortAsync(isRetry: true, ct);
        if (registered)
        {
            // New tools become visible to LLM only after refreshing caches.
            await RefreshToolCachesAsync(ct);
        }
    }

    /// <summary>
    /// Manual MCP reconnect hook (best-effort).
    /// <para/>
    /// WHY:
    /// - Some MCP servers can be flaky at app startup.
    /// - Tool initialization runs once per agent lifetime; if MCP failed once, callers may want a retry.
    /// </summary>
    public async Task<(bool Ok, string? Error)> ReconnectMcpAsync(CancellationToken ct = default)
    {
        try
        {
            await InitializeToolsAsync(ct);
            var registered = await RegisterMcpServersFromConfigurationBestEffortAsync(isRetry: false, ct);
            await RefreshToolCachesAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MCP reconnect failed: {Message}", ex.Message);
            return (false, ex.Message);
        }
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Cleanup first, then base last.
        await DisposeMcpClientsBestEffortAsync();
        await base.OnDeactivateAsync(ct);
    }

    private async Task DisposeMcpClientsBestEffortAsync()
    {
        if (_mcpClients.Count == 0)
            return;

        foreach (var (_, client) in _mcpClients.ToList())
        {
            try
            {
                await client.DisposeAsync();
            }
            catch
            {
                // best-effort only
            }
        }

        _mcpClients.Clear();
    }
}


