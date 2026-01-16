using System.Collections.Concurrent;

namespace Aevatar.Agents.Core.Runtime;

// ============================================================
//  RunManager (Interruptible Runs, MVP)
//
//  中文说明：
//  - 目标：为同一 scope 提供 Latest-wins 的 run 管理：新 run 到来 -> 取消旧 run。
//  - 约束：默认不启用/不影响现有业务；只有调用方显式使用时才生效。
//  - 实现策略：ConcurrentDictionary + 原子替换；取消是 best-effort 协作式。
// ============================================================

public interface IRunManager
{
    RunContext StartOrReplace(string scopeId, string runId, string? reason = null);

    /// <summary>
    /// Interrupt the active run under scopeId (if any).
    /// </summary>
    /// <returns>True if there was an active run and it was canceled.</returns>
    bool Interrupt(string scopeId, string supersededByRunId, string? reason = null);

    bool TryGetActive(string scopeId, out RunContext? context);

    /// <summary>
    /// Clear active run only if it matches the given runId (to avoid removing a newer run).
    /// </summary>
    bool TryClear(string scopeId, string runId);
}

public sealed class RunManager : IRunManager
{
    private readonly ConcurrentDictionary<string, RunContext> _active = new(StringComparer.Ordinal);

    public RunContext StartOrReplace(string scopeId, string runId, string? reason = null)
    {
        scopeId = (scopeId ?? string.Empty).Trim();
        runId = (runId ?? string.Empty).Trim();
        if (scopeId.Length == 0) throw new ArgumentException("scopeId is required", nameof(scopeId));
        if (runId.Length == 0) throw new ArgumentException("runId is required", nameof(runId));

        var next = new RunContext(scopeId, runId);

        // Replace (Latest-wins).
        // NOTE: ConcurrentDictionary.AddOrUpdate returns the *new* value, not the previous one.
        // We must explicitly capture the previous run to cancel it.
        while (true)
        {
            if (_active.TryGetValue(scopeId, out var prev))
            {
                if (_active.TryUpdate(scopeId, next, prev))
                {
                    prev.MarkSuperseded(runId, reason);
                    prev.Cancel();
                    prev.Dispose();
                    return next;
                }

                // Retry on race.
                continue;
            }

            if (_active.TryAdd(scopeId, next))
            {
                return next;
            }

            // Retry on race.
        }
    }

    public bool Interrupt(string scopeId, string supersededByRunId, string? reason = null)
    {
        scopeId = (scopeId ?? string.Empty).Trim();
        supersededByRunId = (supersededByRunId ?? string.Empty).Trim();
        if (scopeId.Length == 0) return false;
        if (supersededByRunId.Length == 0) return false;

        if (!_active.TryGetValue(scopeId, out var cur))
            return false;

        cur.MarkSuperseded(supersededByRunId, reason);
        cur.Cancel();
        return true;
    }

    public bool TryGetActive(string scopeId, out RunContext? context)
    {
        scopeId = (scopeId ?? string.Empty).Trim();
        if (scopeId.Length == 0)
        {
            context = null;
            return false;
        }

        return _active.TryGetValue(scopeId, out context);
    }

    public bool TryClear(string scopeId, string runId)
    {
        scopeId = (scopeId ?? string.Empty).Trim();
        runId = (runId ?? string.Empty).Trim();
        if (scopeId.Length == 0 || runId.Length == 0) return false;

        if (!_active.TryGetValue(scopeId, out var cur))
            return false;

        if (!string.Equals(cur.RunId, runId, StringComparison.Ordinal))
            return false;

        if (_active.TryRemove(scopeId, out var removed))
        {
            removed.Dispose();
            return true;
        }

        return false;
    }
}

// ============================================================
//  RunContextScope (AsyncLocal)
//
//  中文说明：
//  - 让“当前 run”在 async 调用链路中自然可得（类似 IHttpContextAccessor）。
//  - 仅作为上下文承载，不负责创建/取消 run（由 IRunManager 负责）。
// ============================================================

public static class RunContextScope
{
    private static readonly AsyncLocal<RunContext?> Current = new();

    public static class MetadataKeys
    {
        // Keep keys short and stable (cross-boundary, used in EventEnvelope.context_metadata and RpcRequest.metadata).
        public const string RunId = "run_id";
        public const string RunScopeId = "run_scope_id";
    }

    public static RunContext? Value
    {
        get => Current.Value;
        set => Current.Value = value;
    }

    public static IDisposable Begin(RunContext context)
    {
        var prev = Current.Value;
        Current.Value = context;
        return new Restore(prev);
    }

    private sealed class Restore : IDisposable
    {
        private readonly RunContext? _prev;
        public Restore(RunContext? prev) => _prev = prev;
        public void Dispose() => Current.Value = _prev;
    }
}


