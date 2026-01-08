using System.IO;
using System.Net.Http;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.Tool.MCP.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Tool.MCP;

/// <summary>
/// Wrapper around the official MCP SDK's McpClient.
/// Implements IMCPClient to provide a consistent interface for Aevatar.
/// </summary>
public class MCPClientWrapper : IMCPClient
{
    private readonly McpClient _client;
    private readonly ILogger? _logger;
    private readonly MCPServerConfig _config;

    private MCPClientWrapper(McpClient client, MCPServerConfig config, ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>
    /// Creates and connects an MCP client wrapper.
    /// </summary>
    public static async Task<MCPClientWrapper> CreateAsync(
        MCPServerConfig config,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        logger?.LogInformation("Creating MCP client for server: {ServerName} ({TransportType})",
            config.Name, config.TransportType);

        try
        {
            // Create appropriate transport based on config
            IClientTransport transport = config.TransportType switch
            {
                MCPTransportType.Stdio => CreateStdioTransport(config, logger),
                MCPTransportType.Http => CreateHttpTransport(config, logger),
                MCPTransportType.Stream => throw new NotSupportedException(
                    "Stream transport must be created via CreateFromStreamsAsync"),
                _ => throw new NotSupportedException($"Transport type {config.TransportType} is not supported")
            };

            // Create client options
            var options = new McpClientOptions
            {
                InitializationTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
            };

            // Create and connect the client
            var client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken);

            logger?.LogInformation("Successfully connected to MCP server: {ServerName}", config.Name);

            return new MCPClientWrapper(client, config, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create MCP client for server: {ServerName}", config.Name);
            throw;
        }
    }

    /// <summary>
    /// Creates and connects an MCP client wrapper from custom streams.
    /// Useful for SSH, Named Pipes, or other custom transports.
    /// </summary>
    public static async Task<MCPClientWrapper> CreateFromStreamsAsync(
        Stream inputStream,
        Stream outputStream,
        MCPServerConfig config,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(config);

        logger?.LogInformation("Creating MCP client from custom streams for server: {ServerName}", config.Name);

        try
        {
            var transport = new StreamClientTransport(inputStream, outputStream);

            // Create client options
            var options = new McpClientOptions
            {
                InitializationTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
            };

            // Create and connect the client
            var client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken);

            logger?.LogInformation("Successfully connected to MCP server via custom streams: {ServerName}",
                config.Name);

            return new MCPClientWrapper(client, config, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create MCP client from streams for server: {ServerName}", config.Name);
            throw;
        }
    }

    /// <summary>
    /// Creates a stdio transport for local MCP servers (npx/uvx).
    /// </summary>
    private static StdioClientTransport CreateStdioTransport(MCPServerConfig config, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
            throw new ArgumentException("Command is required for Stdio transport", nameof(config));

        logger?.LogDebug("Creating stdio transport: {Command} {Arguments}",
            config.Command, string.Join(" ", config.Arguments ?? []));

        var options = new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = config.Command,
            Arguments = config.Arguments?.ToArray() ?? [],
            WorkingDirectory = config.WorkingDirectory,
            EnvironmentVariables = (config.Environment?
                                        .Where(e => e.Value != null)
                                        .ToDictionary(e => e.Key, e => e.Value!)
                                    ?? new Dictionary<string, string>())!
        };

        return new StdioClientTransport(options);
    }

    /// <summary>
    /// Creates an HTTP transport for remote MCP servers.
    /// </summary>
    private static HttpClientTransport CreateHttpTransport(MCPServerConfig config, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
            throw new ArgumentException("ServerUrl is required for HTTP transport", nameof(config));

        logger?.LogDebug("Creating HTTP transport: {Url}", config.ServerUrl);

        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(config.ServerUrl)
        };

        // Prepare headers
        var headers = new Dictionary<string, string>();

        // ------------------------------------------------------------
        // MCP hosted servers (e.g. mcp.k-dense.ai) require SSE accept.
        //
        // If we don't send `Accept: text/event-stream`, server returns:
        //   406 Not Acceptable: Client must accept text/event-stream
        // ------------------------------------------------------------
        headers["Accept"] = "text/event-stream";

        // Add default headers from config
        if (config.Headers != null)
        {
            foreach (var header in config.Headers)
            {
                if (!string.IsNullOrWhiteSpace(header.Key) && !string.IsNullOrWhiteSpace(header.Value))
                {
                    // Allow user override (including Accept).
                    headers[header.Key] = header.Value;
                }
            }
        }

        // Add Auth token if present
        if (!string.IsNullOrWhiteSpace(config.AuthToken))
        {
            headers["Authorization"] = $"Bearer {config.AuthToken}";
        }

        // Only set AdditionalHeaders if we have any
        if (headers.Count > 0)
        {
            options.AdditionalHeaders = headers;
        }

        return new HttpClientTransport(options);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MCPToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Listing tools from MCP server: {ServerName}", _config.Name);

            var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);

            var mcpTools = tools.Select(ConvertToMCPToolDefinition).ToList();

            _logger?.LogInformation("Retrieved {Count} tools from MCP server: {ServerName}",
                mcpTools.Count, _config.Name);

            return mcpTools;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error listing tools from MCP server: {ServerName}", _config.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MCPToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Calling MCP tool: {ToolName} on server: {ServerName}",
                toolName, _config.Name);

            var result = await _client.CallToolAsync(toolName, parameters, cancellationToken: cancellationToken);

            var mcpResult = ConvertToMCPToolResult(result);

            _logger?.LogDebug("MCP tool call completed: {ToolName}, Success: {IsSuccess}",
                toolName, mcpResult.IsSuccess);

            return mcpResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error calling MCP tool: {ToolName} on server: {ServerName}",
                toolName, _config.Name);

            return new MCPToolResult
            {
                IsSuccess = false,
                Error = $"Tool execution failed: {ex.Message}",
                Content = string.Empty
            };
        }
    }

    /// <inheritdoc />
    public async Task<MCPServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Getting server info from MCP server: {ServerName}", _config.Name);

            // Note: McpClient doesn't have GetServerInfoAsync, we'll use Initialize result
            return new MCPServerInfo
            {
                Name = _config.Name,
                Version = "unknown",
                Description = $"MCP Server ({_config.TransportType})",
                Capabilities = new List<string> { "tools" }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting server info from MCP server: {ServerName}", _config.Name);
            throw;
        }
    }

    /// <summary>
    /// Converts official SDK McpClientTool to MCPToolDefinition.
    /// </summary>
    private MCPToolDefinition ConvertToMCPToolDefinition(McpClientTool tool)
    {
        // McpClientTool is an AIFunction, extract basic info
        return new MCPToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description ?? string.Empty,
            // JsonSchema is the OpenAI-function-style schema; convert it so the LLM knows parameters.
            InputSchema = ConvertToMCPToolSchema(tool.JsonSchema),
            Metadata = new Dictionary<string, object>
            {
                ["OriginalTool"] = tool,
                ["AIFunction"] = true
            }
        };
    }

    /// <summary>
    /// Converts official SDK InputSchema to MCPToolSchema.
    /// </summary>
    private MCPToolSchema ConvertToMCPToolSchema(object? inputSchema)
    {
        if (inputSchema == null)
            return new MCPToolSchema();

        try
        {
            // The SDK's InputSchema is typically a JSON object
            var json = JsonSerializer.Serialize(inputSchema);
            var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (schema == null)
                return new MCPToolSchema();

            var mcpSchema = new MCPToolSchema();

            if (schema.TryGetValue("type", out var typeElement))
                mcpSchema.Type = typeElement.GetString() ?? "object";

            if (schema.TryGetValue("properties", out var propsElement))
            {
                mcpSchema.Properties = new Dictionary<string, MCPPropertySchema>();
                var props = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(propsElement.GetRawText());

                if (props != null)
                {
                    foreach (var (key, value) in props)
                    {
                        mcpSchema.Properties[key] = ConvertToMCPPropertySchema(value);
                    }
                }
            }

            if (schema.TryGetValue("required", out var requiredElement))
            {
                mcpSchema.Required = JsonSerializer.Deserialize<List<string>>(requiredElement.GetRawText());
            }

            return mcpSchema;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error converting input schema for tool, using default schema");
            return new MCPToolSchema();
        }
    }

    /// <summary>
    /// Converts JsonElement to MCPPropertySchema.
    /// </summary>
    private MCPPropertySchema ConvertToMCPPropertySchema(JsonElement element)
    {
        var schema = new MCPPropertySchema();

        if (element.TryGetProperty("type", out var typeElement))
            schema.Type = typeElement.GetString() ?? "string";

        if (element.TryGetProperty("description", out var descElement))
            schema.Description = descElement.GetString();

        if (element.TryGetProperty("enum", out var enumElement))
            schema.Enum = JsonSerializer.Deserialize<List<string>>(enumElement.GetRawText());

        if (element.TryGetProperty("default", out var defaultElement))
            schema.Default = JsonSerializer.Deserialize<object>(defaultElement.GetRawText());

        return schema;
    }

    /// <summary>
    /// Converts official SDK CallToolResult to MCPToolResult.
    /// </summary>
    private MCPToolResult ConvertToMCPToolResult(CallToolResult result)
    {
        var isSuccess = !(result.IsError ?? false);
        var content = ExtractContentFromResult(result);
        var error = (result.IsError ?? false) ? ExtractErrorFromResult(result) : null;

        return new MCPToolResult
        {
            IsSuccess = isSuccess,
            Content = content,
            Error = error,
            Metadata = new Dictionary<string, object>
            {
                ["OriginalResult"] = result
            }
        };
    }

    /// <summary>
    /// Extracts text content from CallToolResult.
    /// </summary>
    private string ExtractContentFromResult(CallToolResult result)
    {
        if (result.Content == null || result.Content.Count == 0)
            return string.Empty;

        // Get text content blocks
        var textBlocks = result.Content.OfType<TextContentBlock>().ToList();

        if (textBlocks.Count > 0)
            return string.Join("\n", textBlocks.Select(b => b.Text));

        // Fallback: serialize the content
        return JsonSerializer.Serialize(result.Content);
    }

    /// <summary>
    /// Extracts error message from CallToolResult.
    /// </summary>
    private string ExtractErrorFromResult(CallToolResult result)
    {
        return result.Content?.FirstOrDefault()?.ToString() ?? "Unknown error";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger?.LogDebug("Disposing MCP client for server: {ServerName}", _config.Name);

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}