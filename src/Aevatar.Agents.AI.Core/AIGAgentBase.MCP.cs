using Microsoft.Extensions.Configuration;

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

    internal IConfiguration? InternalHostConfiguration => HostConfiguration;

    private McpRuntime? _mcpRuntime;
    private McpRuntime McpRuntime => _mcpRuntime ??= new McpRuntime(this);

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
        return await McpRuntime.RegisterFromConfigurationBestEffortAsync(isRetry, cancellationToken);
    }

    /// <summary>
    /// Best-effort retry hook for MCP servers (used by ChatAsync/ChatStreamAsync).
    /// </summary>
    protected async Task TryReconnectMcpOnChatAsync(CancellationToken ct)
    {
        await McpRuntime.TryReconnectOnChatAsync(ct);
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
        return await McpRuntime.ReconnectAsync(ct);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Cleanup first, then base last.
        await McpRuntime.DisposeBestEffortAsync();
        await base.OnDeactivateAsync(ct);
    }
}


