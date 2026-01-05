using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Hooks.BuiltIn;

/// <summary>
/// Warn-only context budget monitor.
///
/// 中文 + ASCII:
/// - 只做“信号”与“可观测”，不做自动 compaction（避免引入复杂度）。
/// - 触发点：BeforeLLMRequest
/// </summary>
public sealed class ContextBudgetMonitorHook : IAevatarAgentHook
{
    private readonly ILogger? _logger;

    public ContextBudgetMonitorHook(ILogger? logger = null)
    {
        _logger = logger;
    }

    public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        var req = context.LlmRequest;
        if (req == null)
            return Task.CompletedTask;

        // Messages + prompts (rough char-based heuristic).
        var messageCount = req.Messages?.Count ?? 0;
        var totalChars = (req.SystemPrompt?.Length ?? 0) + (req.UserPrompt?.Length ?? 0);

        if (req.Messages != null && req.Messages.Count > 0)
        {
            totalChars += req.Messages.Sum(m => m.Content?.Length ?? 0);
        }

        var warnByMessages = messageCount >= context.Policy.ContextMessageWarn;
        var warnByChars = totalChars >= context.Policy.ContextCharsWarn;

        if (!warnByMessages && !warnByChars)
            return Task.CompletedTask;

        context.Metadata["context_budget_warning"] = true;
        context.Metadata["context_budget_message_count"] = messageCount;
        context.Metadata["context_budget_total_chars"] = totalChars;
        context.Metadata["context_budget_message_warn"] = context.Policy.ContextMessageWarn;
        context.Metadata["context_budget_chars_warn"] = context.Policy.ContextCharsWarn;

        _logger?.LogWarning(
            "Context budget warning. AgentId={AgentId} RequestId={RequestId} Messages={Messages} TotalChars={Chars} (WarnMessages>={WarnMessages}, WarnChars>={WarnChars})",
            context.AgentId, context.RequestId, messageCount, totalChars,
            context.Policy.ContextMessageWarn, context.Policy.ContextCharsWarn);

        return Task.CompletedTask;
    }
}
