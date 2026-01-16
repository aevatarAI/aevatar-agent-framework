using System.Linq;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Platform.Core.Tools;

// ============================================================
//  PlatformToolRegistry
//
//  目标：
//  - 以 AI.Core ToolManager 为执行后端
//  - 对外提供“可发现 + 策略过滤 + 执行前校验”
// ============================================================
public sealed class PlatformToolRegistry
{
    private readonly IAevatarToolManager _toolManager;
    private readonly PlatformToolPolicy _policy;

    public PlatformToolRegistry(IAevatarToolManager toolManager, PlatformToolPolicy policy)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAllAsync(CancellationToken ct = default)
    {
        var tools = await _toolManager.GetAvailableToolsAsync(ct) ?? Array.Empty<ToolDefinition>();
        return tools
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListAllowedAsync(CancellationToken ct = default)
    {
        var tools = await _toolManager.GetAvailableToolsAsync(ct) ?? Array.Empty<ToolDefinition>();
        var allowed = new List<ToolDefinition>();

        foreach (var tool in tools)
        {
            if (_policy.EvaluateTool(tool).Allowed)
                allowed.Add(tool);
        }

        return allowed
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public void ApplyPolicyToContext(ToolExecutionContext context)
    {
        if (context == null)
            return;

        context.AllowDangerousTools = _policy.AllowDangerousTools;
        context.AllowInternalTools = _policy.AllowInternalTools;
    }

    public async Task<ToolExecutionResult> ExecuteWithPolicyAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return BuildDeniedResult("tool_name_empty");

        var tool = await _toolManager.GetToolAsync(toolName, ct);
        if (tool == null)
            return await _toolManager.ExecuteToolAsync(toolName, parameters, context, ct);

        var decision = _policy.EvaluateTool(tool);
        if (!decision.Allowed)
            return BuildDeniedResult(decision.Reason);

        var execContext = EnsureContext(context);
        ApplyPolicyToContext(execContext);

        using var cts = CreateTimeoutCts(toolName, ct, out var effectiveToken);
        return await _toolManager.ExecuteToolAsync(toolName, parameters, execContext, effectiveToken);
    }

    private ToolExecutionContext EnsureContext(ToolExecutionContext? context)
    {
        if (context == null)
        {
            return new ToolExecutionContext
            {
                ToolManager = _toolManager
            };
        }

        context.ToolManager = _toolManager;
        return context;
    }

    private CancellationTokenSource? CreateTimeoutCts(
        string toolName,
        CancellationToken ct,
        out CancellationToken effectiveToken)
    {
        effectiveToken = ct;
        if (!_policy.TryGetToolTimeout(toolName, out var timeout))
            return null;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        effectiveToken = cts.Token;
        return cts;
    }

    private static ToolExecutionResult BuildDeniedResult(string reason)
    {
        return new ToolExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = reason ?? "tool_denied",
            Content = reason ?? "tool_denied",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }
}


