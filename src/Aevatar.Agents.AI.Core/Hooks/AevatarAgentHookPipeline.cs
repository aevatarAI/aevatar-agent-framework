using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Hooks;

/// <summary>
/// Executes agent hooks in a deterministic, best-effort pipeline.
///
/// 中文 + ASCII:
/// - 排序：Priority 升序，其次 Name（稳定）。
/// - 禁用：Options.DisabledHooks（类似 oh-my-opencode disabled_hooks）。
/// - best-effort：单个 hook 失败只记录日志，不阻塞主链路。
/// </summary>
public sealed class AevatarAgentHookPipeline
{
    private readonly ILogger<AevatarAgentHookPipeline> _logger;
    private readonly AevatarAgentHookOptions _options;
    private readonly IReadOnlyList<IAevatarAgentHook> _hooks;

    public AevatarAgentHookPipeline(
        IEnumerable<IAevatarAgentHook>? hooks = null,
        AevatarAgentHookOptions? options = null,
        ILogger<AevatarAgentHookPipeline>? logger = null)
    {
        _logger = logger ?? NullLogger<AevatarAgentHookPipeline>.Instance;
        _options = options ?? new AevatarAgentHookOptions();
        _hooks = BuildHookList(hooks ?? Array.Empty<IAevatarAgentHook>(), _options, _logger);
    }

    public IReadOnlyList<IAevatarAgentHook> Hooks => _hooks;

    public bool HasHooks => _hooks.Count > 0;

    public AevatarAgentHookPolicy CreatePolicySnapshot(bool allowInternalTools, bool allowDangerousTools)
    {
        // Keep it bounded and sane; hooks should never receive negative budgets.
        var maxToolChars = Math.Clamp(_options.MaxToolOutputChars, 1_000, 512_000);
        var msgWarn = Math.Clamp(_options.ContextMessageWarn, 1, 10_000);
        var charsWarn = Math.Clamp(_options.ContextCharsWarn, 1_000, 5_000_000);

        return new AevatarAgentHookPolicy(
            AllowInternalTools: allowInternalTools,
            AllowDangerousTools: allowDangerousTools,
            MaxToolOutputChars: maxToolChars,
            ContextMessageWarn: msgWarn,
            ContextCharsWarn: charsWarn);
    }

    public Task RunBeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("BeforeLLMRequest", context, cancellationToken,
            (h, ctx, ct) => h.BeforeLLMRequestAsync(ctx, ct));

    public Task RunOnSessionStartAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("SessionStart", context, cancellationToken,
            (h, ctx, ct) => h.OnSessionStartAsync(ctx, ct));

    public Task RunOnSessionEndAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("SessionEnd", context, cancellationToken,
            (h, ctx, ct) => h.OnSessionEndAsync(ctx, ct));

    public Task RunOnStopAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("Stop", context, cancellationToken,
            (h, ctx, ct) => h.OnStopAsync(ctx, ct));

    public Task RunAfterLLMResponseAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("AfterLLMResponse", context, cancellationToken,
            (h, ctx, ct) => h.AfterLLMResponseAsync(ctx, ct));

    public Task RunBeforeToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("BeforeToolExecute", context, cancellationToken,
            (h, ctx, ct) => h.BeforeToolExecuteAsync(ctx, ct));

    public Task RunAfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunStageAsync("AfterToolExecute", context, cancellationToken,
            (h, ctx, ct) => h.AfterToolExecuteAsync(ctx, ct));

    public Task RunOnErrorAsync(AevatarAgentHookContext context, Exception exception, CancellationToken cancellationToken)
        => RunStageAsync("OnError", context, cancellationToken,
            (h, ctx, ct) => h.OnErrorAsync(ctx, exception, ct));

    private async Task RunStageAsync(
        string stage,
        AevatarAgentHookContext context,
        CancellationToken cancellationToken,
        Func<IAevatarAgentHook, AevatarAgentHookContext, CancellationToken, Task> invoke)
    {
        if (_hooks.Count == 0)
            return;

        foreach (var hook in _hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            try
            {
                await invoke(hook, context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Hook failed. Hook={Hook} Stage={Stage} AgentId={AgentId} RequestId={RequestId}",
                    hook.Name, stage, context.AgentId, context.RequestId);
            }
            finally
            {
                sw.Stop();
                _logger.LogDebug("Hook executed. Hook={Hook} Stage={Stage} ElapsedMs={ElapsedMs} RequestId={RequestId}",
                    hook.Name, stage, sw.ElapsedMilliseconds, context.RequestId);
            }
        }
    }

    private static IReadOnlyList<IAevatarAgentHook> BuildHookList(
        IEnumerable<IAevatarAgentHook> hooks,
        AevatarAgentHookOptions options,
        ILogger logger)
    {
        // Apply "last wins" on duplicate names (deterministic given DI registration order).
        var byName = new Dictionary<string, IAevatarAgentHook>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in hooks.Where(h => h != null))
        {
            var name = string.IsNullOrWhiteSpace(h.Name) ? h.GetType().Name : h.Name;
            if (byName.ContainsKey(name))
            {
                logger.LogWarning("Duplicate hook name detected. Using last registration. HookName={HookName}",
                    name);
            }

            byName[name] = h;
        }

        // Disabled hooks (case-insensitive).
        var disabled = new HashSet<string>(
            options.DisabledHooks.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var filtered = byName.Values
            .Where(h => !IsDisabled(h, disabled))
            .OrderBy(h => h.Priority)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return filtered;
    }

    private static bool IsDisabled(IAevatarAgentHook hook, HashSet<string> disabled)
    {
        if (disabled.Count == 0)
            return false;

        var name = hook.Name;
        var typeName = hook.GetType().Name;
        return disabled.Contains(name) || disabled.Contains(typeName);
    }
}