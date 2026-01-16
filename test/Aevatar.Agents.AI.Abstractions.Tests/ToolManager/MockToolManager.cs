using System.Collections.Concurrent;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;

namespace Aevatar.Agents.AI.Abstractions.Tests.ToolManager;

/// <summary>
/// Mock tool manager for testing purposes
/// </summary>
public class MockToolManager : IAevatarToolManager
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private readonly List<(string toolName, Dictionary<string, object> parameters)> _executionHistory = new();
    private bool _throwOnExecute;
    private Exception? _exceptionToThrow;
    private string? _mockExecutionResult;
    
    public IReadOnlyList<(string toolName, Dictionary<string, object> parameters)> ExecutionHistory => _executionHistory;
    
    public void RegisterTool(ToolDefinition tool)
    {
        _tools[tool.Name] = tool;
    }
    
    public void ConfigureToThrowOnExecute(Exception exception)
    {
        _throwOnExecute = true;
        _exceptionToThrow = exception;
    }
    
    public void SetMockExecutionResult(string result)
    {
        _mockExecutionResult = result;
    }
    
    public Task RegisterToolAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _tools[tool.Name] = tool;
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ToolDefinition>>(_tools.Values.Where(t => t.IsEnabled).ToList());
    }

    public Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _executionHistory.Add((toolName, parameters));
        
        if (_throwOnExecute && _exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }
        
        if (!_tools.ContainsKey(toolName))
        {
            return Task.FromResult(new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' not found",
                ToolName = toolName
            });
        }
        
        var tool = _tools[toolName];
        if (!tool.IsEnabled)
        {
            return Task.FromResult(new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is disabled",
                ToolName = toolName
            });
        }
        
        return Task.FromResult(new ToolExecutionResult
        {
            IsSuccess = true,
            Content = _mockExecutionResult ?? $"Mock result for {toolName}",
            ToolName = toolName
        });
    }
    
    public async Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var tools = await GetAvailableToolsAsync(cancellationToken);
        var definitions = new List<AevatarFunctionDefinition>();
        
        foreach (var tool in tools)
        {
            definitions.Add(new AevatarFunctionDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = ConvertToFunctionParameters(tool.Parameters)
            });
        }
        
        return definitions;
    }
    
    private Dictionary<string, AevatarParameterDefinition> ConvertToFunctionParameters(ToolParameters? parameters)
    {
        var result = new Dictionary<string, AevatarParameterDefinition>();
        
        if (parameters?.Items == null)
            return result;
        
        foreach (var param in parameters.Items)
        {
            result[param.Key] = new AevatarParameterDefinition
            {
                Type = param.Value.Type ?? "string",
                Description = param.Value.Description,
                Required = parameters.Required?.Contains(param.Key) ?? false,
                Default = param.Value.DefaultValue,
                Enum = param.Value.Enum?.Select(e => e.ToString()).ToList()
            };
        }
        
        return result;
    }
}
