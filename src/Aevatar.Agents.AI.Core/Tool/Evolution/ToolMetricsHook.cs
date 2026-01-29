using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Tool.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  ToolMetricsHook
//
//  中文 + ASCII:
//  - 聚合工具指标并按阈值发布快照。
//  - best-effort，不阻塞工具执行。
// ============================================================
/// <summary>
/// Tool metrics aggregation hook (best-effort).
/// </summary>
public sealed class ToolMetricsHook : IAevatarAgentHook
{
    private readonly ToolEvolutionOptions _options;
    private readonly ToolMetricsStore _store;
    private readonly Func<ToolMetricsSnapshot, CancellationToken, Task>? _publishSnapshot;
    private readonly ILogger? _logger;

    public ToolMetricsHook(
        ToolEvolutionOptions options,
        ToolMetricsStore store,
        Func<ToolMetricsSnapshot, CancellationToken, Task>? publishSnapshot,
        ILogger? logger = null)
    {
        _options = options ?? new ToolEvolutionOptions();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _publishSnapshot = publishSnapshot;
        _logger = logger;
    }

    public int Priority => 210;

    public async Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.EnableMetrics)
            return;

        var feedback = BuildFeedback(context);
        if (feedback == null)
            return;

        var snapshot = _store.Record(feedback, _options.MetricsSnapshotEveryNCalls);
        if (snapshot == null || _publishSnapshot == null)
            return;

        try
        {
            await _publishSnapshot(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ToolMetricsHook publish snapshot failed (best-effort).");
        }
    }

    private static ToolExecutionFeedback? BuildFeedback(AevatarAgentHookContext context)
    {
        var result = context.ToolResult;
        if (result == null)
            return null;

        return new ToolExecutionFeedback
        {
            ToolName = context.ToolName ?? result.ToolName ?? string.Empty,
            ToolVersion = TryGetMetadata(context, "tool_version"),
            ToolCallId = context.ToolCallId ?? result.ToolCallId ?? string.Empty,
            RequestId = context.RequestId,
            AgentId = context.AgentId,
            SessionId = context.ChatRequest?.GetSessionId() ?? string.Empty,
            Success = result.IsSuccess,
            ErrorCode = TryGetMetadata(context, "error_code"),
            ErrorMessage = result.ErrorMessage ?? string.Empty,
            DurationMs = (long)(result.Duration?.ToTimeSpan().TotalMilliseconds ?? 0),
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }

    private static string TryGetMetadata(AevatarAgentHookContext context, string key)
    {
        if (context.Metadata.TryGetValue(key, out var value) && value != null)
            return value.ToString() ?? string.Empty;

        return string.Empty;
    }
}
