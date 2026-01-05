using System.Collections.Generic;

namespace Aevatar.Agents.AI.Core.Hooks;

/// <summary>
/// Hook (harness) options.
///
/// 中文 + ASCII:
/// - 模拟 oh-my-opencode 的 `disabled_hooks` 体验：允许通过配置禁用指定 hook。
/// - 预算项用于“收敛型治理”（例如截断 tool 输出），默认值应保守且安全。
/// </summary>
public sealed class AevatarAgentHookOptions
{
    /// <summary>
    /// Disabled hook names (case-insensitive match by convention).
    /// Example: ["ToolOutputTruncationHook", "ContextBudgetMonitorHook"]
    /// </summary>
    public List<string> DisabledHooks { get; set; } = new();

    /// <summary>
    /// Max characters to keep in tool result content before truncation.
    /// Default: 16000.
    ///
    /// 中文 + ASCII:
    /// - This budget is used by the built-in <c>ToolOutputTruncationHook</c>.
    /// - AIGAgentBase's <c>skills_load</c> tool also uses this as its default <c>max_chars</c>,
    ///   so host can tune the output budget in ONE place (avoid duplicated knobs).
    /// </summary>
    public int MaxToolOutputChars { get; set; } = 16_000;

    /// <summary>
    /// Soft warning threshold for total message count in a single LLM request.
    /// Default: 64.
    /// </summary>
    public int ContextMessageWarn { get; set; } = 64;

    /// <summary>
    /// Soft warning threshold for total character count of messages in a single LLM request.
    /// Default: 200000.
    /// </summary>
    public int ContextCharsWarn { get; set; } = 200_000;
}