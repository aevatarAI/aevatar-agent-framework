namespace Aevatar.Agents.Core.Runtime;

// ============================================================
//  RunContext (Interruptible Runs, MVP)
//
//  中文说明：
//  - Run 是一次可取消的“长任务”执行单元（例如：LLM 流式输出、工具调用、编排步骤）。
//  - scopeId 用于限定 Latest-wins 的作用域（通常是 sessionId 或 agentId）。
//  - 取消采用协作式 CancellationToken：不做硬 kill，只要求下游在边界处观察 token。
// ============================================================

public sealed class RunContext : IDisposable
{
    public string ScopeId { get; }
    public string RunId { get; }
    public DateTimeOffset StartedAtUtc { get; }

    public string? SupersededByRunId { get; private set; }
    public string? Reason { get; private set; }

    private readonly CancellationTokenSource _cts;

    public CancellationToken Token => _cts.Token;
    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public RunContext(string scopeId, string runId)
    {
        ScopeId = (scopeId ?? string.Empty).Trim();
        RunId = (runId ?? string.Empty).Trim();
        StartedAtUtc = DateTimeOffset.UtcNow;

        if (ScopeId.Length == 0) throw new ArgumentException("scopeId is required", nameof(scopeId));
        if (RunId.Length == 0) throw new ArgumentException("runId is required", nameof(runId));

        _cts = new CancellationTokenSource();
    }

    public void MarkSuperseded(string? supersededByRunId, string? reason)
    {
        SupersededByRunId = string.IsNullOrWhiteSpace(supersededByRunId) ? null : supersededByRunId.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Cancel()
    {
        // Idempotent: CancellationTokenSource.Cancel() is safe to call multiple times.
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}


