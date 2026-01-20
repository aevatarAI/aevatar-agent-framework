using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Core.Hooks.Policy;

/// <summary>
/// Deny tool execution by name (policy hook).
/// </summary>
public sealed class ToolDenyListHook : IAevatarAgentHook
{
    private readonly ToolDenyListHookOptions _options;
    private readonly ILogger<ToolDenyListHook> _logger;

    public ToolDenyListHook(IOptions<ToolDenyListHookOptions> options, ILogger<ToolDenyListHook>? logger = null)
        : this(options?.Value ?? new ToolDenyListHookOptions(), logger)
    {
    }

    public ToolDenyListHook(ToolDenyListHookOptions options, ILogger<ToolDenyListHook>? logger = null)
    {
        _options = options ?? new ToolDenyListHookOptions();
        _logger = logger ?? NullLogger<ToolDenyListHook>.Instance;
    }

    public Task BeforeToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ToolName))
            return Task.CompletedTask;

        if (_options.DeniedTools.Count == 0)
            return Task.CompletedTask;

        if (_options.DeniedTools.Any(name =>
                string.Equals(name?.Trim(), context.ToolName, StringComparison.OrdinalIgnoreCase)))
        {
            var reason = string.IsNullOrWhiteSpace(_options.DenyReason)
                ? $"Tool '{context.ToolName}' denied by policy."
                : _options.DenyReason;
            context.DenyTool(reason);
            _logger.LogInformation("Tool execution denied by policy hook. Tool={ToolName}", context.ToolName);
        }

        return Task.CompletedTask;
    }
}
