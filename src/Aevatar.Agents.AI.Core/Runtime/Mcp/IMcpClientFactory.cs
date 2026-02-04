using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IMcpClientFactory
{
    Task<IMCPClient> CreateAsync(MCPServerConfig config, ILogger logger, CancellationToken ct);
}

internal sealed class DefaultMcpClientFactory : IMcpClientFactory
{
    public async Task<IMCPClient> CreateAsync(MCPServerConfig config, ILogger logger, CancellationToken ct)
    {
        var client = await MCPClientWrapper.CreateAsync(config, logger, ct);
        return client;
    }
}

