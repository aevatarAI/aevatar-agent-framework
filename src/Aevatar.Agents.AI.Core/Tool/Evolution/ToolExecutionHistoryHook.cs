using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  ToolExecutionHistoryHook
//
//  中文 + ASCII:
//  - 从 HookContext 抽取二值反馈，写入事件/MemoryStore。
//  - best-effort，不阻塞工具执行。
// ============================================================
/// <summary>
/// Tool execution feedback hook (best-effort).
/// </summary>
public sealed class ToolExecutionHistoryHook : IAevatarAgentHook
{
    private readonly ToolEvolutionOptions _options;
    private readonly Func<ToolExecutionFeedback, CancellationToken, Task>? _publishEvent;
    private readonly Func<ToolExecutionFeedback, CancellationToken, Task>? _appendMemory;
    private readonly ILogger? _logger;

    public ToolExecutionHistoryHook(
        ToolEvolutionOptions options,
        Func<ToolExecutionFeedback, CancellationToken, Task>? publishEvent,
        Func<ToolExecutionFeedback, CancellationToken, Task>? appendMemory,
        ILogger? logger = null)
    {
        _options = options ?? new ToolEvolutionOptions();
        _publishEvent = publishEvent;
        _appendMemory = appendMemory;
        _logger = logger;
    }

    public int Priority => 200;

    public async Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.EnableFeedbackHooks)
            return;

        var toolResult = context.ToolResult;
        if (toolResult == null)
            return;

        var feedback = new ToolExecutionFeedback
        {
            ToolName = context.ToolName ?? toolResult.ToolName ?? string.Empty,
            ToolVersion = TryGetMetadata(context, "tool_version"),
            ToolCallId = context.ToolCallId ?? toolResult.ToolCallId ?? string.Empty,
            RequestId = context.RequestId,
            AgentId = context.AgentId,
            SessionId = context.ChatRequest?.GetSessionId() ?? string.Empty,
            Success = toolResult.IsSuccess,
            ErrorCode = TryGetMetadata(context, "error_code"),
            ErrorMessage = toolResult.ErrorMessage ?? string.Empty,
            DurationMs = (long)(toolResult.Duration?.ToTimeSpan().TotalMilliseconds ?? 0),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (context.Metadata.Count > 0)
        {
            foreach (var (k, v) in context.Metadata)
            {
                if (v == null) continue;
                feedback.Metadata[k] = v.ToString() ?? string.Empty;
            }
        }

        await EmitFeedbackAsync(feedback, cancellationToken);
    }

    private async Task EmitFeedbackAsync(ToolExecutionFeedback feedback, CancellationToken ct)
    {
        try
        {
            if (_options.EnableMemoryStoreAppend && _appendMemory != null)
            {
                await _appendMemory(feedback, ct);
            }

            if (_options.EnableFeedbackEvents && _publishEvent != null)
            {
                await _publishEvent(feedback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ToolExecutionHistoryHook failed (best-effort).");
        }
    }

    private static string TryGetMetadata(AevatarAgentHookContext context, string key)
    {
        if (context.Metadata.TryGetValue(key, out var value) && value != null)
            return value.ToString() ?? string.Empty;

        return string.Empty;
    }
}
