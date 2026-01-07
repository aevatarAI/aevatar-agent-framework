using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.MCP.Abstractions;
using Aevatar.Agents.AI.WithTool.MCP.Models;
using Aevatar.Agents.AI.WithTool.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.MCP;

/// <summary>
/// Adapts MCP tools to Aevatar's tool system.
/// Converts MCPToolDefinition to ToolDefinition and handles execution.
/// </summary>
public class MCPToolAdapter
{
    private readonly IMCPClient _mcpClient;
    private readonly ILogger<MCPToolAdapter>? _logger;

    public MCPToolAdapter(IMCPClient mcpClient, ILogger<MCPToolAdapter>? logger = null)
    {
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _logger = logger;
    }

    /// <summary>
    /// Converts an MCP tool definition to an Aevatar tool definition.
    /// </summary>
    public ToolDefinition CreateToolDefinition(MCPToolDefinition mcpTool)
    {
        ArgumentNullException.ThrowIfNull(mcpTool);

        return new ToolDefinition
        {
            Name = mcpTool.Name,
            Description = mcpTool.Description ?? string.Empty,
            Category = ToolCategory.Integration, // MCP tools are integration tools
            Version = "1.0",
            IsEnabled = true,
            CanBeOverridden = false,
            // ------------------------------------------------------------
            // Safety:
            // - MCP tools are external-by-definition and may cause side effects.
            // - Mark as dangerous so callers can gate them via AllowDangerousTools.
            // ------------------------------------------------------------
            IsDangerous = true,
            Parameters = ConvertSchemaToParameters(mcpTool.InputSchema),
            ExecuteAsync = CreateExecutionDelegate(mcpTool.Name),
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "MCP",
                ["OriginalSchema"] = mcpTool.InputSchema
            }
        };
    }

    /// <summary>
    /// Converts MCP schema to Aevatar tool parameters.
    /// </summary>
    private ToolParameters ConvertSchemaToParameters(MCPToolSchema schema)
    {
        var parameters = new ToolParameters();

        if (schema.Properties != null)
        {
            foreach (var prop in schema.Properties)
            {
                parameters.Items[prop.Key] = new ToolParameter
                {
                    Type = prop.Value.Type,
                    Description = prop.Value.Description ?? string.Empty,
                    Required = schema.Required?.Contains(prop.Key) ?? false,
                    DefaultValue = prop.Value.Default,
                    Enum = prop.Value.Enum?.Cast<object>().ToList()
                };
            }
        }

        if (schema.Required != null)
        {
            parameters.Required = schema.Required;
        }

        return parameters;
    }

    /// <summary>
    /// Creates an execution delegate for the MCP tool.
    /// </summary>
    private Func<Dictionary<string, object>, ToolExecutionContext?, CancellationToken, Task<IMessage>> 
        CreateExecutionDelegate(string toolName)
    {
        return async (parameters, context, cancellationToken) =>
        {
            try
            {
                _logger?.LogDebug("Executing MCP tool: {ToolName} with parameters: {@Parameters}", 
                    toolName, parameters);

                // Convert parameters to nullable dictionary for MCP client
                var mcpParameters = parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object?)kvp.Value
                );

                // Call MCP server
                var mcpResult = await _mcpClient.CallToolAsync(toolName, mcpParameters, cancellationToken);

                // Convert result to AevatarAIToolResult.cs
                var result = mcpResult.IsSuccess
                    ? AevatarAIToolResult.CreateSuccess(mcpResult.Content)
                    : AevatarAIToolResult.CreateFailure(mcpResult.Error ?? "Unknown error");

                // Add metadata
                result.Metadata["Source"] = "MCP";
                result.Metadata["ToolName"] = toolName;

                _logger?.LogDebug("MCP tool execution completed: {ToolName}, Success: {IsSuccess}", 
                    toolName, result.Success);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing MCP tool: {ToolName}", toolName);
                
                var errorResult = AevatarAIToolResult.CreateFailure($"MCP tool execution failed: {ex.Message}");
                errorResult.Metadata["Source"] = "MCP";
                errorResult.Metadata["ToolName"] = toolName;
                errorResult.Metadata["Exception"] = ex.GetType().Name;

                return errorResult;
            }
        };
    }
}
