using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Hooks.BuiltIn;

/// <summary>
/// Emit ExecutionTraceEvent for LLM/tool lifecycle (best-effort).
/// Session lifecycle events are opt-in and should be emitted at explicit
/// session boundaries (e.g., session create/close), not per request.
/// </summary>
public sealed class ExecutionTraceProgressHook : IAevatarAgentHook
{
    private readonly Func<ExecutionTraceEvent, CancellationToken, Task> _publish;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, byte> _sessionStarts = new(StringComparer.Ordinal);
    private readonly bool _emitSessionLifecycle;
    private static int _suppressLogCount;

    public int Priority => -1000;

    public ExecutionTraceProgressHook(
        Func<ExecutionTraceEvent, CancellationToken, Task> publish,
        ILogger? logger = null,
        bool emitSessionLifecycle = false)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _logger = logger;
        _emitSessionLifecycle = emitSessionLifecycle;
    }

    public Task OnSessionStartAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        if (!_emitSessionLifecycle)
            return Task.CompletedTask;

        if (!TryMarkSessionStarted(context.RequestId))
            return Task.CompletedTask;

        return EmitSessionAsync(
            context,
            ExecutionTraceEventPhase.SessionStart,
            ExecutionTraceEventStatus.Running,
            progress: 0,
            cancellationToken);
    }

    public Task OnStopAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        if (!_emitSessionLifecycle)
            return Task.CompletedTask;

        var status = MapStopStatus(context.StopStatus);
        var task = EmitSessionAsync(
            context,
            ExecutionTraceEventPhase.SessionStop,
            status,
            progress: status == ExecutionTraceEventStatus.Completed ? 1.0 : null,
            cancellationToken);
        ClearSessionStarted(context.RequestId);
        return task;
    }

    public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => IsTraceSuppressed(context)
            ? Task.CompletedTask
            : _emitSessionLifecycle
                ? EmitSessionIfMissingAndLlmAsync(context, cancellationToken)
                : EmitLlmAsync(
                    context,
                    ExecutionTraceEventPhase.LlmRequest,
                    ExecutionTraceEventStatus.Running,
                    cancellationToken);

    private async Task EmitSessionIfMissingAndLlmAsync(
        AevatarAgentHookContext context,
        CancellationToken cancellationToken)
    {
        if (TryMarkSessionStarted(context.RequestId))
        {
            await EmitSessionAsync(
                context,
                ExecutionTraceEventPhase.SessionStart,
                ExecutionTraceEventStatus.Running,
                progress: 0,
                cancellationToken);
        }

        await EmitLlmAsync(
            context,
            ExecutionTraceEventPhase.LlmRequest,
            ExecutionTraceEventStatus.Running,
            cancellationToken);
    }

    public Task AfterLLMResponseAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        return EmitLlmAsync(
            context,
            ExecutionTraceEventPhase.LlmResponse,
            ExecutionTraceEventStatus.Completed,
            cancellationToken);
    }

    public Task BeforeToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        return EmitToolAsync(
            context,
            ExecutionTraceEventPhase.ToolStart,
            ExecutionTraceEventStatus.Running,
            cancellationToken);
    }

    public Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        var status = context.ToolResult?.IsSuccess == false
            ? ExecutionTraceEventStatus.Failed
            : ExecutionTraceEventStatus.Completed;
        return EmitToolAsync(
            context,
            ExecutionTraceEventPhase.ToolEnd,
            status,
            cancellationToken);
    }

    public Task BeforeEventHandlerAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        return EmitEventHandlerAsync(
            context,
            ExecutionTraceEventPhase.EventHandlerStart,
            ExecutionTraceEventStatus.Running,
            cancellationToken);
    }

    public Task AfterEventHandlerAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        var status = context.EventHandlerException == null
            ? ExecutionTraceEventStatus.Completed
            : ExecutionTraceEventStatus.Failed;
        return EmitEventHandlerAsync(
            context,
            ExecutionTraceEventPhase.EventHandlerEnd,
            status,
            cancellationToken);
    }

    public Task OnErrorAsync(AevatarAgentHookContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (IsTraceSuppressed(context))
            return Task.CompletedTask;
        return EmitErrorAsync(context, exception, cancellationToken);
    }

    private Task EmitSessionAsync(
        AevatarAgentHookContext context,
        string phase,
        string status,
        double? progress,
        CancellationToken cancellationToken)
    {
        var evt = CreateBaseEvent(context, phase, status, progress);
        evt.NodeId = $"session:{context.RequestId}";
        evt.Message = phase;

        if (context.Duration.HasValue)
        {
            evt.Fields[ExecutionTraceEventFields.DurationMs] =
                ExecutionTraceEventFieldValue.FromLong((long)context.Duration.Value.TotalMilliseconds);
        }

        return PublishBestEffortAsync(evt, cancellationToken);
    }

    private static bool IsTraceSuppressed(AevatarAgentHookContext context)
    {
        if (context == null)
            return false;

        var chatSuppressed = TryReadSuppressFlag(context.ChatRequest?.Context);
        var llmSuppressed = TryReadSuppressFlag(context.LlmRequest?.Context);
        var metaSuppressed = context.Metadata.TryGetValue(AIGAgentKeys.SuppressExecutionTrace, out var meta) &&
                             IsTruthy(meta);

        var suppressed = chatSuppressed || llmSuppressed || metaSuppressed;
        if (suppressed && Interlocked.Increment(ref _suppressLogCount) <= 3)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId = context.ChatRequest?.GetSessionId() ?? string.Empty,
                    runId = context.RequestId,
                    hypothesisId = "H4",
                    location = "ExecutionTraceProgressHook.cs:IsTraceSuppressed",
                    message = "trace_suppressed",
                    data = new
                    {
                        agentId = context.AgentId,
                        chatSuppressed,
                        llmSuppressed,
                        metaSuppressed
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }

        return suppressed;
    }

    private static bool TryReadSuppressFlag(Google.Protobuf.Collections.MapField<string, string>? context)
    {
        if (context == null || context.Count == 0)
            return false;

        if (!context.TryGetValue(AIGAgentKeys.SuppressExecutionTrace, out var value))
            return false;

        return IsTruthy(value);
    }

    private static bool TryReadSuppressFlag(Dictionary<string, object>? context)
    {
        if (context == null || context.Count == 0)
            return false;

        if (!context.TryGetValue(AIGAgentKeys.SuppressExecutionTrace, out var value))
            return false;

        return IsTruthy(value);
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("yes", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private Task EmitLlmAsync(
        AevatarAgentHookContext context,
        string phase,
        string status,
        CancellationToken cancellationToken)
    {
        var evt = CreateBaseEvent(context, phase, status, null);
        evt.NodeId = "llm";
        evt.Message = phase;

        var model = context.LlmRequest?.Settings?.ModelId;
        if (!string.IsNullOrWhiteSpace(model))
        {
            evt.Fields[ExecutionTraceEventFields.LlmModel] =
                ExecutionTraceEventFieldValue.FromString(model);
        }

        if (context.LlmResponse?.Usage != null)
        {
            evt.Fields[ExecutionTraceEventFields.PromptTokens] =
                ExecutionTraceEventFieldValue.FromLong(context.LlmResponse.Usage.PromptTokens);
            evt.Fields[ExecutionTraceEventFields.CompletionTokens] =
                ExecutionTraceEventFieldValue.FromLong(context.LlmResponse.Usage.CompletionTokens);
            evt.Fields[ExecutionTraceEventFields.TokensUsed] =
                ExecutionTraceEventFieldValue.FromLong(context.LlmResponse.Usage.TotalTokens);
        }

        return PublishBestEffortAsync(evt, cancellationToken);
    }

    private Task EmitToolAsync(
        AevatarAgentHookContext context,
        string phase,
        string status,
        CancellationToken cancellationToken)
    {
        var evt = CreateBaseEvent(context, phase, status, null);
        evt.NodeId = $"tool:{context.ToolCallId ?? context.ToolName ?? "unknown"}";
        evt.Message = BuildToolMessage(context, phase);

        if (!string.IsNullOrWhiteSpace(context.ToolName))
        {
            evt.Fields[ExecutionTraceEventFields.ToolName] =
                ExecutionTraceEventFieldValue.FromString(context.ToolName);
        }

        if (!string.IsNullOrWhiteSpace(context.ToolCallId))
        {
            evt.Fields[ExecutionTraceEventFields.ToolCallId] =
                ExecutionTraceEventFieldValue.FromString(context.ToolCallId);
        }

        if (!string.IsNullOrWhiteSpace(context.ToolResult?.ErrorMessage))
        {
            evt.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(Trim(context.ToolResult.ErrorMessage, 400));
        }

        if (context.ToolResult?.Duration != null)
        {
            evt.Fields[ExecutionTraceEventFields.DurationMs] =
                ExecutionTraceEventFieldValue.FromLong((long)context.ToolResult.Duration.ToTimeSpan().TotalMilliseconds);
        }

        return PublishBestEffortAsync(evt, cancellationToken);
    }

    private Task EmitEventHandlerAsync(
        AevatarAgentHookContext context,
        string phase,
        string status,
        CancellationToken cancellationToken)
    {
        var evt = CreateBaseEvent(context, phase, status, null);
        var handlerName = !string.IsNullOrWhiteSpace(context.EventHandlerName)
            ? context.EventHandlerName
            : (context.EventType ?? "handler");
        evt.NodeId = $"handler:{handlerName}";
        evt.Message = $"{phase}:{handlerName}";

        if (!string.IsNullOrWhiteSpace(context.EventType))
        {
            evt.Fields[ExecutionTraceEventFields.EventType] =
                ExecutionTraceEventFieldValue.FromString(context.EventType);
        }

        if (!string.IsNullOrWhiteSpace(context.EventHandlerName))
        {
            evt.Fields[ExecutionTraceEventFields.HandlerName] =
                ExecutionTraceEventFieldValue.FromString(context.EventHandlerName);
        }

        if (!string.IsNullOrWhiteSpace(context.EventHandlerType))
        {
            evt.Fields[ExecutionTraceEventFields.HandlerType] =
                ExecutionTraceEventFieldValue.FromString(context.EventHandlerType);
        }

        if (context.EventHandlerDuration.HasValue)
        {
            evt.Fields[ExecutionTraceEventFields.DurationMs] =
                ExecutionTraceEventFieldValue.FromLong((long)context.EventHandlerDuration.Value.TotalMilliseconds);
        }

        if (context.EventHandlerException != null)
        {
            evt.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(Trim(context.EventHandlerException.Message, 400));
        }

        return PublishBestEffortAsync(evt, cancellationToken);
    }

    private Task EmitErrorAsync(
        AevatarAgentHookContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var evt = CreateBaseEvent(context, ExecutionTraceEventPhase.Error, ExecutionTraceEventStatus.Failed, null);
        evt.NodeId = $"error:{context.RequestId}";
        evt.Message = Trim(exception.Message, 400);
        evt.Fields[ExecutionTraceEventFields.Error] =
            ExecutionTraceEventFieldValue.FromString(Trim(exception.Message, 400));
        return PublishBestEffortAsync(evt, cancellationToken);
    }

    private ExecutionTraceEvent CreateBaseEvent(
        AevatarAgentHookContext context,
        string phase,
        string status,
        double? progress)
    {
        var sessionId = TryGetContextValue(
            context.ChatRequest,
            ChatRequest.SessionIdKey,
            ChatRequest.SessionIdKeyCamel);
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? context.RequestId : sessionId;

        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = string.Empty,
            NodeId = phase
        };

        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(status);
        evt.Fields[ExecutionTraceEventFields.Phase] =
            ExecutionTraceEventFieldValue.FromString(phase);
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString(resolvedSessionId);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(context.RequestId);
        evt.Fields[ExecutionTraceEventFields.AgentId] =
            ExecutionTraceEventFieldValue.FromString(context.AgentId);
        evt.Fields[ExecutionTraceEventFields.MessageId] =
            ExecutionTraceEventFieldValue.FromString(context.RequestId);

        if (progress.HasValue)
        {
            evt.Fields[ExecutionTraceEventFields.Progress] =
                ExecutionTraceEventFieldValue.FromDouble(progress.Value);
        }

        return evt;
    }

    private static string? TryGetContextValue(ChatRequest? request, params string[] keys)
    {
        if (request?.Context == null || request.Context.Count == 0)
            return null;

        foreach (var k in keys)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (!request.Context.TryGetValue(k, out var v)) continue;
            if (string.IsNullOrWhiteSpace(v)) continue;
            return v.Trim();
        }

        return null;
    }

    private bool TryMarkSessionStarted(string? requestId)
    {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0)
            return false;

        return _sessionStarts.TryAdd(id, 0);
    }

    private void ClearSessionStarted(string? requestId)
    {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0)
            return;

        _sessionStarts.TryRemove(id, out _);
    }

    private static string NormalizeRequestId(string? requestId)
        => (requestId ?? string.Empty).Trim();

    private async Task PublishBestEffortAsync(ExecutionTraceEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            await _publish(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ExecutionTraceProgressHook publish failed.");
        }
    }

    private static string MapStopStatus(AevatarAgentHookStopStatus? status)
    {
        return status switch
        {
            AevatarAgentHookStopStatus.Completed => ExecutionTraceEventStatus.Completed,
            AevatarAgentHookStopStatus.Aborted => ExecutionTraceEventStatus.Cancelled,
            AevatarAgentHookStopStatus.Error => ExecutionTraceEventStatus.Failed,
            _ => ExecutionTraceEventStatus.Completed
        };
    }

    private static string BuildToolMessage(AevatarAgentHookContext ctx, string phase)
    {
        var tool = ctx.ToolName ?? "tool";
        return phase == ExecutionTraceEventPhase.ToolStart
            ? $"tool.start:{tool}"
            : $"tool.end:{tool}";
    }

    private static string Trim(string? value, int maxChars)
    {
        var text = (value ?? string.Empty).Replace("\r", "").Trim();
        if (text.Length <= maxChars)
            return text;
        return text[..maxChars];
    }
}
