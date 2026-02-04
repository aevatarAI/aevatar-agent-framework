using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IMcpRuntimeHost
{
    ILogger Logger { get; }

    bool EnableMcpServers { get; }
    bool McpRetryOnEachChat { get; }
    TimeSpan McpRetryMinInterval { get; }
    IConfiguration? HostConfiguration { get; }

    IAevatarToolManager ToolManager { get; }
    Task InitializeToolsAsync(CancellationToken ct);
    Task RefreshToolCachesAsync(CancellationToken ct);

    Task RegisterMcpToolsAsync(
        string serverKey,
        IMCPClient mcpClient,
        string toolNamePrefix,
        MCPServerConfig config,
        CancellationToken ct);
}

