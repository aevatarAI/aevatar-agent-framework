using System.Diagnostics;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;

namespace ScientificResearchAssistant.Streaming;

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
        var toolCallId = Guid.NewGuid().ToString("N");

        if (sink != null)
        {
            await Safe(async () => await sink.EmitToolStartAsync(toolCallId, toolName, cancellationToken));
        }

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


