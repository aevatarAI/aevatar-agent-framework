using System;
using System.Collections.Generic;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;

namespace Aevatar.Agents.AI.Core.Hooks;

/// <summary>
/// Hook execution context for a single agent request/tool loop.
///
/// 中文 + ASCII:
/// - 这是 Hook 与核心执行链之间的数据平面（data plane）。
/// - 设计目标：信息足够、边界清晰、可观测；hook 只能“收敛”，不能“扩权”。
/// </summary>
public sealed class AevatarAgentHookContext
{
    public AevatarAgentHookContext(
        string agentId,
        string agentType,
        string requestId,
        AevatarAgentHookPolicy policy)
    {
        AgentId = string.IsNullOrWhiteSpace(agentId) ? "unknown" : agentId;
        AgentType = string.IsNullOrWhiteSpace(agentType) ? "unknown" : agentType;
        RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        Policy = policy;
    }

    /// <summary>Agent identity snapshot.</summary>
    public string AgentId { get; }

    /// <summary>Agent type snapshot.</summary>
    public string AgentType { get; }

    /// <summary>Request correlation id (e.g. ChatRequest.RequestId).</summary>
    public string RequestId { get; }

    /// <summary>
    /// Read-only policy snapshot.
    /// IMPORTANT: hooks must not widen permissions beyond this policy.
    /// </summary>
    public AevatarAgentHookPolicy Policy { get; }

    // ------------------------------------------------------------
    // LLM call context (may be reused across tool-call loop rounds)
    // ------------------------------------------------------------
    public AevatarLLMRequest? LlmRequest { get; set; }
    public AevatarLLMResponse? LlmResponse { get; set; }

    // ------------------------------------------------------------
    // Tool execution context (per tool call)
    // ------------------------------------------------------------
    public string? ToolName { get; set; }
    public Dictionary<string, object>? ToolArguments { get; set; }
    public ToolExecutionResult? ToolResult { get; set; }

    /// <summary>
    /// Free-form metadata for observability (truncation flags, downgrade reason, etc).
    /// Keep values small; do NOT store secrets here.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Read-only safety policy snapshot for hooks.
///
/// 中文 + ASCII:
/// - 由核心执行链创建并注入到 HookContext。
/// - Hook 只能依据此策略做“收敛型”修改（例如截断输出、拒绝执行），不能把权限开大。
/// </summary>
public readonly record struct AevatarAgentHookPolicy(
    bool AllowInternalTools,
    bool AllowDangerousTools,
    int MaxToolOutputChars,
    int ContextMessageWarn,
    int ContextCharsWarn);