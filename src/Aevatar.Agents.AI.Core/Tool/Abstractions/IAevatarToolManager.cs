using Aevatar.Agents.AI.Abstractions; // For AevatarFunctionDefinition?
using Aevatar.Agents.AI.Tool.Messages; // For AevatarToolExecutionContext

namespace Aevatar.Agents.AI.Tool.Abstractions;

/// <summary>
/// Tool manager interface - simplified version
/// Provides core functionality for Agent tool extensions
/// </summary>
public interface IAevatarToolManager
{
    /// <summary>
    /// Register tool (core method)
    /// </summary>
    Task RegisterToolAsync(
        ToolDefinition tool,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all available tools (core method)
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute tool (core method)
    /// </summary>
    Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate function definitions for LLM Function Calling (core method)
    /// </summary>
    Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get single tool (optional implementation)
    /// </summary>
    async Task<ToolDefinition?> GetToolAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        var tools = await GetAvailableToolsAsync(cancellationToken);
        return tools.FirstOrDefault(t => t.Name == toolName);
    }
}