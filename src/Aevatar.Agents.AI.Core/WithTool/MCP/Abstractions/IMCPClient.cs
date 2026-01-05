using Aevatar.Agents.AI.WithTool.MCP.Models;

namespace Aevatar.Agents.AI.WithTool.MCP.Abstractions;

/// <summary>
/// Interface for MCP (Model Context Protocol) client operations.
/// This is the abstraction layer that wraps the official MCP SDK.
/// </summary>
public interface IMCPClient : IAsyncDisposable
{
    /// <summary>
    /// Lists all available tools from the MCP server.
    /// </summary>
    Task<IReadOnlyList<MCPToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a specific tool on the MCP server.
    /// </summary>
    /// <param name="toolName">Name of the tool to call</param>
    /// <param name="parameters">Tool parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result</returns>
    Task<MCPToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the server information.
    /// </summary>
    Task<MCPServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default);
}
