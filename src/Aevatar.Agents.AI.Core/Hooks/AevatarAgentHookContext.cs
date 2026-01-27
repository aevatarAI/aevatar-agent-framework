using System;
using System.Collections.Generic;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Messages;

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
    private bool _toolDenied;
    private string? _toolDenyReason;

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
    /// Chat request snapshot (optional, may be null for internal/tool-only calls).
    /// </summary>
    public ChatRequest? ChatRequest { get; set; }

    /// <summary>
    /// Whether this hook context is created for streaming flow.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Stop status for session-level hooks (optional).
    /// </summary>
    public AevatarAgentHookStopStatus? StopStatus { get; set; }

    /// <summary>
    /// Stop reason summary (optional).
    /// </summary>
    public string? StopReason { get; set; }

    /// <summary>
    /// Total duration for the session-level hook (optional).
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Exception snapshot for session stop (optional).
    /// </summary>
    public Exception? StopException { get; set; }

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
    public string? ToolCallId { get; set; }

    // ------------------------------------------------------------
    // Event handler context (per handler)
    //
    // 中文 + ASCII:
    // - 仅记录轻量可观测信息，不携带事件体，避免序列化负担。
    // ------------------------------------------------------------
    public string? EventId { get; set; }
    public string? EventType { get; set; }
    public string? EventHandlerName { get; set; }
    public string? EventHandlerType { get; set; }
    public TimeSpan? EventHandlerDuration { get; set; }
    public Exception? EventHandlerException { get; set; }

    /// <summary>
    /// Free-form metadata for observability (truncation flags, downgrade reason, etc).
    /// Keep values small; do NOT store secrets here.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------
    // Typed helper APIs (avoid stringly-typed metadata protocols)
    // ------------------------------------------------------------

    /// <summary>
    /// Deny executing the current tool call (purely restrictive).
    ///
    /// 中文 + ASCII:
    /// - This is a typed helper that replaces the implicit protocol of setting
    ///   <c>Metadata["deny_tool"]</c> / <c>Metadata["deny_reason"]</c>.
    /// - For backward compatibility, this method still writes those keys via <see cref="AIGAgentKeys"/>.
    /// </summary>
    public void DenyTool(string? reason = null)
    {
        _toolDenied = true;
        _toolDenyReason = string.IsNullOrWhiteSpace(reason) ? "Denied" : reason.Trim();

        // Keep observability and backward compatibility.
        Metadata[AIGAgentKeys.HookDenyTool] = true;
        Metadata[AIGAgentKeys.HookDenyReason] = _toolDenyReason;
    }

    /// <summary>
    /// Try get deny reason for tool execution (typed-first, metadata fallback for compatibility).
    /// </summary>
    public bool TryGetToolDenyReason(out string? reason)
    {
        if (_toolDenied)
        {
            reason = _toolDenyReason;
            return true;
        }

        if (Metadata.TryGetValue(AIGAgentKeys.HookDenyTool, out var denyObj) &&
            denyObj is bool deny &&
            deny)
        {
            reason = Metadata.TryGetValue(AIGAgentKeys.HookDenyReason, out var r) ? r?.ToString() : null;
            return true;
        }

        reason = null;
        return false;
    }
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

/// <summary>
/// Stop status for a single agent session (ChatAsync/ChatStreamAsync).
/// </summary>
public enum AevatarAgentHookStopStatus
{
    Completed = 0,
    Aborted = 1,
    Error = 2
}