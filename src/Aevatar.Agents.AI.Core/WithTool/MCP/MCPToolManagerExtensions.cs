using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.MCP.Abstractions;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.MCP;

/// <summary>
/// Extension methods for registering MCP tools with AevatarToolManager.
/// </summary>
public static class MCPToolManagerExtensions
{
    /// <summary>
    /// Registers all tools from an MCP server to the tool manager.
    /// </summary>
    /// <param name="toolManager">The tool manager instance</param>
    /// <param name="serverUrl">MCP server identifier (e.g., "mcp://github" or package name)</param>
    /// <param name="mcpClient">MCP client instance (required)</param>
    /// <param name="config">Optional server configuration (will be inferred if null)</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task RegisterMCPServerAsync(
        this IAevatarToolManager toolManager,
        string serverUrl,
        IMCPClient mcpClient,
        MCPServerConfig? config = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentNullException.ThrowIfNull(mcpClient);

        logger?.LogInformation("Registering MCP tools from server: {ServerUrl}", serverUrl);

        try
        {
            // Discover tools from MCP server
            var mcpTools = await mcpClient.ListToolsAsync(cancellationToken);

            logger?.LogInformation("Discovered {Count} tools from MCP server: {ServerUrl}", 
                mcpTools.Count, serverUrl);

            // Create adapter
            var adapter = new MCPToolAdapter(mcpClient, logger as ILogger<MCPToolAdapter>);

            // Register each tool
            var registeredCount = 0;
            foreach (var mcpTool in mcpTools)
            {
                try
                {
                    var toolDef = adapter.CreateToolDefinition(mcpTool);
                    await toolManager.RegisterToolAsync(toolDef, cancellationToken);
                    registeredCount++;

                    logger?.LogDebug("Registered MCP tool: {ToolName} from {ServerUrl}", 
                        mcpTool.Name, serverUrl);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to register MCP tool: {ToolName} from {ServerUrl}", 
                        mcpTool.Name, serverUrl);
                }
            }

            logger?.LogInformation("Successfully registered {RegisteredCount}/{TotalCount} MCP tools from {ServerUrl}",
                registeredCount, mcpTools.Count, serverUrl);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to register MCP tools from server: {ServerUrl}", serverUrl);
            throw;
        }
    }

    /// <summary>
    /// Registers an MCP server using npx with automatic client creation.
    /// </summary>
    /// <param name="toolManager">The tool manager instance</param>
    /// <param name="packageName">NPM package name (e.g., "@modelcontextprotocol/server-github")</param>
    /// <param name="serverName">Optional server name for identification</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task RegisterMCPServerViaNpxAsync(
        this IAevatarToolManager toolManager,
        string packageName,
        string? serverName = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var config = MCPServerConfig.CreateNpxConfig(packageName, serverName);
        
        logger?.LogInformation("Creating MCP client for npx package: {PackageName}", packageName);
        
        var mcpClient = await MCPClientWrapper.CreateAsync(
            config, 
            logger as ILogger<MCPClientWrapper>, 
            cancellationToken);

        await toolManager.RegisterMCPServerAsync(
            packageName,
            mcpClient,
            config,
            logger,
            cancellationToken);
    }

    /// <summary>
    /// Registers an MCP server using uvx with automatic client creation.
    /// </summary>
    /// <param name="toolManager">The tool manager instance</param>
    /// <param name="packageName">Python package name</param>
    /// <param name="serverName">Optional server name for identification</param>
    /// <param name="logger">Optional logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task RegisterMCPServerViaUvxAsync(
        this IAevatarToolManager toolManager,
        string packageName,
        string? serverName = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var config = MCPServerConfig.CreateUvxConfig(packageName, serverName);
        
        logger?.LogInformation("Creating MCP client for uvx package: {PackageName}", packageName);
        
        var mcpClient = await MCPClientWrapper.CreateAsync(
            config, 
            logger as ILogger<MCPClientWrapper>, 
            cancellationToken);

        await toolManager.RegisterMCPServerAsync(
            packageName,
            mcpClient,
            config,
            logger,
            cancellationToken);
    }

    /// <summary>
    /// Registers multiple MCP servers at once.
    /// </summary>
    public static async Task RegisterMCPServersAsync(
        this IAevatarToolManager toolManager,
        IEnumerable<(string ServerUrl, IMCPClient Client, MCPServerConfig? Config)> servers,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolManager);
        ArgumentNullException.ThrowIfNull(servers);

        var tasks = servers.Select(server => 
            toolManager.RegisterMCPServerAsync(
                server.ServerUrl, 
                server.Client, 
                server.Config,
                logger,
                cancellationToken));

        await Task.WhenAll(tasks);
    }
}
