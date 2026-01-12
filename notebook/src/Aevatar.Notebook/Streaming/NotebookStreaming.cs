using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Notebook.Streaming;

// ============================================================
//  Notebook Streaming (MVP)
//
//  目标：
//  - 让前端在 LLM 触发 tool calling 时能实时看到「工具名 + 进度」。
//
//  设计：
//  - 通过 ToolManager wrapper 在 ExecuteToolAsync 前后发出事件
//  - 事件通过 AsyncLocal sink 绑定到当前 HTTP streaming 请求（/api/chat/stream）
//
//  注意：
//  - 必须 best-effort：任何 UI 事件失败都不能影响 tool 执行本身。
//  - 不允许把 tool 参数/结果全文塞给前端（可能巨大/含敏感信息）。
// ============================================================

internal interface INotebookStreamEventSink
{
    Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct);

    Task EmitToolEndAsync(
        string toolCallId,
        string toolName,
        bool success,
        long durationMs,
        string? error,
        CancellationToken ct);
}

internal static class NotebookStreamEventContext
{
    private static readonly AsyncLocal<INotebookStreamEventSink?> CurrentSink = new();

    public static INotebookStreamEventSink? Current
    {
        get => CurrentSink.Value;
        set => CurrentSink.Value = value;
    }
}

/// <summary>
/// NDJSON sink for /api/chat/stream.
/// </summary>
internal sealed class NdjsonNotebookStreamEventSink : INotebookStreamEventSink
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _lock;

    public NdjsonNotebookStreamEventSink(StreamWriter writer, JsonSerializerOptions json, SemaphoreSlim? writeLock = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _json = json ?? throw new ArgumentNullException(nameof(json));
        _lock = writeLock ?? new SemaphoreSlim(1, 1);
    }

    public Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct)
    {
        return SafeWriteAsync(new { type = "tool_start", toolCallId, toolName }, ct);
    }

    public Task EmitToolEndAsync(
        string toolCallId,
        string toolName,
        bool success,
        long durationMs,
        string? error,
        CancellationToken ct)
    {
        return SafeWriteAsync(new
        {
            type = "tool_end",
            toolCallId,
            toolName,
            success,
            durationMs,
            error = Trunc(error, 200)
        }, ct);
    }

    private async Task SafeWriteAsync(object payload, CancellationToken ct)
    {
        try
        {
            await _lock.WaitAsync(ct);
            try
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(payload, _json));
                await _writer.FlushAsync();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch
        {
            // Best-effort: never fail tool execution due to UI event emit.
        }
    }

    private static string Trunc(string? s, int maxChars)
    {
        var x = (s ?? string.Empty).Replace("\r", "").Trim();
        if (x.Length <= maxChars)
            return x;
        return x[..maxChars];
    }
}

/// <summary>
/// ToolManager wrapper that emits tool progress events to current stream sink (AsyncLocal).
/// </summary>
internal sealed class NotebookToolManager : IAevatarToolManager
{
    private readonly IAevatarToolManager _inner;

    public NotebookToolManager(IAevatarToolManager inner)
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
        var sink = NotebookStreamEventContext.Current;
        var toolCallId = Guid.NewGuid().ToString("N");

        if (sink != null)
        {
            await sink.EmitToolStartAsync(toolCallId, toolName, cancellationToken);
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

                await sink.EmitToolEndAsync(toolCallId, toolName, success, durationMs, error, cancellationToken);
            }
        }
    }
}


