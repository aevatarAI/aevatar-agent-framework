using System.Collections.Concurrent;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.Tool.MCP.Models;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tests.TestKit;

internal sealed class FakeMcpClient : IMCPClient
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<MCPToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MCPToolDefinition>>(Array.Empty<MCPToolDefinition>());

    public Task<MCPToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new MCPToolResult { IsSuccess = true, Content = "{}" });

    public Task<MCPServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new MCPServerInfo { Name = "fake", Version = "0" });
}

internal sealed class FakeMcpClientFactory : IMcpClientFactory
{
    public int CreateCalls { get; private set; }
    public ConcurrentBag<FakeMcpClient> Clients { get; } = new();

    public Task<IMCPClient> CreateAsync(MCPServerConfig config, ILogger logger, CancellationToken ct)
    {
        CreateCalls++;
        var c = new FakeMcpClient();
        Clients.Add(c);
        return Task.FromResult<IMCPClient>(c);
    }
}

