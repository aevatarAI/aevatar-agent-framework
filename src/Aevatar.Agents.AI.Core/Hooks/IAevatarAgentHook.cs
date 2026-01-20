using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.AI.Core.Hooks;

/// <summary>
/// Agent hook for cross-cutting concerns (harness layer).
///
/// 中文 + ASCII:
/// - 目标：把“稳定性/上下文治理/工具输出治理/兼容性修复”等横切逻辑从业务 Agent 中抽离出来。
/// - 原则：best-effort（hook 失败不阻塞主链路），且只能收敛权限（不能扩大危险能力）。
/// </summary>
// ReSharper disable InconsistentNaming
public interface IAevatarAgentHook
{
    /// <summary>
    /// Stable hook name used for ordering and disabling.
    /// Recommendation: use type name (default) unless you need a custom stable id.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Deterministic execution priority. Smaller runs earlier.
    /// Default: <see cref="int.MaxValue"/>.
    /// </summary>
    int Priority => int.MaxValue;

    /// <summary>
    /// Called before sending request to LLM provider (best-effort).
    /// </summary>
    Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called when a chat session starts (best-effort).
    /// </summary>
    Task OnSessionStartAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called when a chat session ends (best-effort).
    /// </summary>
    Task OnSessionEndAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called when the agent loop stops (best-effort).
    /// </summary>
    Task OnStopAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called after receiving response from LLM provider (best-effort).
    /// </summary>
    Task AfterLLMResponseAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called right before executing a tool (best-effort).
    /// </summary>
    Task BeforeToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called right after executing a tool (best-effort).
    /// </summary>
    Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Called when an error happens during LLM/tool pipeline (best-effort).
    /// </summary>
    Task OnErrorAsync(AevatarAgentHookContext context, Exception exception, CancellationToken cancellationToken)
        => Task.CompletedTask;
}