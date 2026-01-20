using System.Diagnostics;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;

namespace VibeResearching.Streaming;

// ============================================================
//  Research Streaming Context (AG-UI)
//
//  目标：
//  - 在 ChatStreamAsync 期间，把 tool calling 的开始/结束变成“可观察事件”
//  - 事件通过 AsyncLocal 绑定到当前请求（HTTP SSE /api/sessions/{id}/agui/events）
//
//  原则：
//  - best-effort：任何 UI emit 失败都不能影响 tool 执行
//  - 不把巨大的 tool 输出直接灌给前端（默认只给摘要/截断）
// ============================================================

public interface IResearchStreamEventSink
{
    Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct);

    Task EmitToolProgressAsync(
        string toolCallId,
        string toolName,
        string message,
        CancellationToken ct);

    Task EmitToolEndAsync(
        string toolCallId,
        string toolName,
        bool success,
        long durationMs,
        string? error,
        string? resultPreview,
        CancellationToken ct);
}

public static class ResearchStreamEventContext
{
    private static readonly AsyncLocal<IResearchStreamEventSink?> CurrentSink = new();

    public static IResearchStreamEventSink? Current
    {
        get => CurrentSink.Value;
        set => CurrentSink.Value = value;
    }
}

/// <summary>
/// ToolManager wrapper that emits tool progress events to current stream sink (AsyncLocal).
/// </summary>
public sealed class ResearchToolManager : IAevatarToolManager
{
    private readonly IAevatarToolManager _inner;

    public ResearchToolManager(IAevatarToolManager inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task RegisterToolAsync(ToolDefinition tool, CancellationToken cancellationToken = default) =>
        _inner.RegisterToolAsync(tool, cancellationToken);

    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default) =>
        _inner.GetAvailableToolsAsync(cancellationToken);

    public Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        _inner.GenerateFunctionDefinitionsAsync(cancellationToken);

    public async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var sink = ResearchStreamEventContext.Current;
        var toolCallId = context?.ToolCallId;
        if (string.IsNullOrWhiteSpace(toolCallId))
        {
            toolCallId = Guid.NewGuid().ToString("N");
            if (context != null)
                context.ToolCallId = toolCallId;
        }
        if (context != null && string.IsNullOrWhiteSpace(context.ToolName))
            context.ToolName = toolName;
        var startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long lastProgressAtMs = startedAtMs;
        string? lastProgressMsg = null;

        // Progress emitter (throttled, best-effort).
        async Task EmitProgressAsync(string msg, CancellationToken ct)
        {
            msg = Normalize(msg) ?? string.Empty;
            if (msg.Length == 0) return;

            // Keep payload bounded to avoid giant UI snapshots.
            if (msg.Length > 2000) msg = msg[..2000];

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var since = now - lastProgressAtMs;
            if (since < 1200 && string.Equals(lastProgressMsg, msg, StringComparison.Ordinal))
                return;
            if (since < 800)
                return;

            lastProgressAtMs = now;
            lastProgressMsg = msg;

            if (sink != null)
                await Safe(async () => await sink.EmitToolProgressAsync(toolCallId, toolName, msg, ct));
        }

        if (sink != null)
        {
            await Safe(async () => await sink.EmitToolStartAsync(toolCallId, toolName, cancellationToken));
        }

        Func<string, CancellationToken, Task>? prevProgress = null;

        // Attach progress callback to tool execution context (optional).
        if (context != null)
        {
            prevProgress = context.ReportProgressAsync;
            context.ReportProgressAsync = async (msg, ct) =>
            {
                try
                {
                    if (prevProgress != null)
                        await prevProgress(msg, ct);
                }
                catch
                {
                    // best-effort
                }

                try { await EmitProgressAsync(msg, ct); } catch { /* best-effort */ }
            };
        }

        // Heartbeat: if no progress for a while, emit an "elapsed" update so UI doesn't look frozen.
        using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = sink == null
            ? Task.CompletedTask
            : Task.Run(async () =>
            {
                while (!hbCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(2500, hbCts.Token);
                    }
                    catch
                    {
                        break;
                    }

                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (now - lastProgressAtMs < 3000)
                        continue;

                    var elapsedSec = Math.Max(0, (now - startedAtMs) / 1000);
                    await EmitProgressAsync($"running… {elapsedSec}s", hbCts.Token);
                }
            }, hbCts.Token);

        ToolExecutionResult? result = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = await _inner.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
            return result;
        }
        finally
        {
            sw.Stop();

            // Stop heartbeat and restore context.
            try { hbCts.Cancel(); } catch { /* ignore */ }
            try { await heartbeat; } catch { /* ignore */ }
            if (context != null)
                context.ReportProgressAsync = prevProgress;

            if (sink != null)
            {
                var success = result?.IsSuccess ?? false;
                var durationMs = result != null
                    ? (long)result.Duration.ToTimeSpan().TotalMilliseconds
                    : (long)sw.Elapsed.TotalMilliseconds;

                var error = success ? null : (result?.ErrorMessage ?? "tool failed");
                var preview = BuildPreview(result?.Content);

                await Safe(async () => await sink.EmitToolEndAsync(
                    toolCallId,
                    toolName,
                    success,
                    durationMs,
                    Normalize(error),
                    Normalize(preview),
                    cancellationToken));
            }
        }
    }

    private static async Task Safe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // ignored (best-effort)
        }
    }

    private static string? BuildPreview(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        return s;
    }

    private static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Replace("\r", "").Trim();
    }
}


