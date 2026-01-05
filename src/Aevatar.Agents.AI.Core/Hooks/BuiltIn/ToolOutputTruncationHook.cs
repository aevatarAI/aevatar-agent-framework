namespace Aevatar.Agents.AI.Core.Hooks.BuiltIn;

/// <summary>
/// Truncate oversized tool outputs to keep LLM context bounded.
///
/// 中文 + ASCII:
/// - 这是一个“收敛型”hook：只会减少输出，不会扩大权限。
/// - 触发点：AfterToolExecute
/// </summary>
public sealed class ToolOutputTruncationHook : IAevatarAgentHook
{
    public Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        var result = context.ToolResult;
        if (result == null)
            return Task.CompletedTask;

        var content = result.Content ?? string.Empty;
        var max = context.Policy.MaxToolOutputChars;
        if (max <= 0 || content.Length <= max)
            return Task.CompletedTask;

        var marker = $"\n\n...[TRUNCATED tool output: total={content.Length} chars, max={max}]...\n\n";
        if (marker.Length >= max)
        {
            // Budget is too small to even include a marker; fall back to hard cut.
            var hard = content[..max];
            result.Content = hard;
            context.Metadata["tool_output_truncated"] = true;
            context.Metadata["tool_output_total_chars"] = content.Length;
            context.Metadata["tool_output_kept_chars"] = hard.Length;
            if (!string.IsNullOrWhiteSpace(context.ToolName))
            {
                context.Metadata["tool_output_tool"] = context.ToolName!;
            }
            return Task.CompletedTask;
        }

        var budget = Math.Max(0, max - marker.Length);
        var head = budget / 2;
        var tail = budget - head;

        head = Math.Clamp(head, 0, content.Length);
        tail = Math.Clamp(tail, 0, content.Length - head);

        var truncated = content[..head] + marker + (tail > 0 ? content[^tail..] : string.Empty);

        // Apply in-place so the caller sees the truncated payload.
        result.Content = truncated;

        // Observability flags (no secrets).
        context.Metadata["tool_output_truncated"] = true;
        context.Metadata["tool_output_total_chars"] = content.Length;
        context.Metadata["tool_output_kept_chars"] = truncated.Length;
        if (!string.IsNullOrWhiteSpace(context.ToolName))
        {
            context.Metadata["tool_output_tool"] = context.ToolName!;
        }

        return Task.CompletedTask;
    }
}


