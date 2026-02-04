using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.TestKit;

internal sealed class NoopToolManager : IAevatarToolManager
{
    public Task RegisterToolAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolExecutionResult { ToolName = toolName, IsSuccess = false, ErrorMessage = "noop" });

    public Task<ToolDefinition?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
        => Task.FromResult<ToolDefinition?>(null);

    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ToolDefinition>>(Array.Empty<ToolDefinition>());

    public Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AevatarFunctionDefinition>>(Array.Empty<AevatarFunctionDefinition>());
}

internal sealed class RecordingToolManager : IAevatarToolManager
{
    private readonly List<ToolDefinition> _tools;

    public RecordingToolManager(params ToolDefinition[] initial)
    {
        _tools = initial?.ToList() ?? new List<ToolDefinition>();
    }

    public Task RegisterToolAsync(ToolDefinition toolDefinition, CancellationToken cancellationToken = default)
    {
        _tools.Add(toolDefinition);
        return Task.CompletedTask;
    }

    public Task UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        _tools.RemoveAll(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ToolExecutionResult { ToolName = toolName, IsSuccess = true, Content = "{}" });

    public Task<ToolDefinition?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
        => Task.FromResult<ToolDefinition?>(_tools.FirstOrDefault(t => t.Name == toolName));

    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ToolDefinition>>(_tools.ToList());

    public Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var defs = _tools.Select(t => new AevatarFunctionDefinition
        {
            Name = t.Name,
            Description = t.Description
        }).ToList();

        return Task.FromResult<IReadOnlyList<AevatarFunctionDefinition>>(defs);
    }
}

