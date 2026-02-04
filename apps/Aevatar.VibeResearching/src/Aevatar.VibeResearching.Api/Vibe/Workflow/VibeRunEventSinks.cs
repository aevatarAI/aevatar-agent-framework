using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Researching.Runtime;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Workflow;
using Aevatar.Agents.Cognitive.Streaming;
using Google.Protobuf.WellKnownTypes;

namespace VibeResearching.Api.Vibe.Workflow;

// ============================================================
//  Tool progress -> AG-UI projection (CUSTOM events)
// ============================================================
internal sealed class AgUiResearchStreamEventSink : IResearchStreamEventSink
{
    private readonly IResearchingRuntime _runtime;
    private readonly string _sessionId;
    private readonly BroadcastEventHub<AgUiEvent> _hub;
    private readonly string _threadId;
    private readonly string _runId;

    public AgUiResearchStreamEventSink(
        IResearchingRuntime runtime,
        string sessionId,
        BroadcastEventHub<AgUiEvent> hub,
        string threadId,
        string runId)
    {
        _runtime = runtime;
        _sessionId = sessionId;
        _hub = hub;
        _threadId = threadId;
        _runId = runId;
    }

    public async Task EmitToolStartAsync(string toolCallId, string toolName, CancellationToken ct)
    {
        var isMcp = await _runtime.IsMcpToolAsync(_sessionId, toolName, ct);
        var messageId = $"msg:{_threadId}:assistant:{_runId}";

        PublishTraceEvents(BuildToolTraceEvent(
            phase: ExecutionTraceEventPhase.ToolStart,
            status: ExecutionTraceEventStatus.Running,
            toolCallId: toolCallId,
            toolName: toolName,
            messageId: messageId,
            message: $"tool.start:{toolName}"));

        _hub.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "aevatar.scientific.tool_start",
            Value = new { threadId = _threadId, runId = _runId, toolCallId, toolName, isMcp }
        });
    }

    public Task EmitToolProgressAsync(string toolCallId, string toolName, string message, CancellationToken ct)
    {
        var messageId = $"msg:{_threadId}:assistant:{_runId}";
        var payload = (message ?? string.Empty).Replace("\r", "").Trim();
        if (payload.Length > 2000) payload = payload[..2000];

        PublishTraceEvents(BuildToolTraceEvent(
            phase: ExecutionTraceEventPhase.ToolProgress,
            status: ExecutionTraceEventStatus.Running,
            toolCallId: toolCallId,
            toolName: toolName,
            messageId: messageId,
            message: payload));

        return Task.CompletedTask;
    }

    public async Task EmitToolEndAsync(
        string toolCallId,
        string toolName,
        bool success,
        long durationMs,
        string? error,
        string? resultPreview,
        CancellationToken ct)
    {
        var isMcp = await _runtime.IsMcpToolAsync(_sessionId, toolName, ct);
        var messageId = $"msg:{_threadId}:assistant:{_runId}";

        PublishTraceEvents(BuildToolTraceEvent(
            phase: ExecutionTraceEventPhase.ToolEnd,
            status: success ? ExecutionTraceEventStatus.Completed : ExecutionTraceEventStatus.Failed,
            toolCallId: toolCallId,
            toolName: toolName,
            messageId: messageId,
            message: resultPreview ?? (error ?? string.Empty),
            durationMs: durationMs,
            error: error));

        _hub.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "aevatar.scientific.tool_end",
            Value = new
            {
                threadId = _threadId,
                runId = _runId,
                toolCallId,
                toolName,
                isMcp,
                success,
                durationMs,
                error,
                resultPreview
            }
        });
    }

    private void PublishTraceEvents(ExecutionTraceEvent traceEvent)
    {
        var mapped = AgUiTraceProjector.Map(traceEvent);
        for (var i = 0; i < mapped.Count; i++)
        {
            _hub.Publish(mapped[i]);
        }
    }

    private ExecutionTraceEvent BuildToolTraceEvent(
        string phase,
        string status,
        string toolCallId,
        string toolName,
        string messageId,
        string message,
        long? durationMs = null,
        string? error = null)
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = message ?? string.Empty,
            NodeId = $"tool:{toolCallId}"
        };

        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(status);
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString(_sessionId);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(_runId);
        evt.Fields[ExecutionTraceEventFields.ToolName] =
            ExecutionTraceEventFieldValue.FromString(toolName);
        evt.Fields[ExecutionTraceEventFields.ToolCallId] =
            ExecutionTraceEventFieldValue.FromString(toolCallId);
        evt.Fields[ExecutionTraceEventFields.MessageId] =
            ExecutionTraceEventFieldValue.FromString(messageId);
        evt.Fields[ExecutionTraceEventFields.Phase] =
            ExecutionTraceEventFieldValue.FromString(phase);

        if (durationMs.HasValue)
        {
            evt.Fields[ExecutionTraceEventFields.DurationMs] =
                ExecutionTraceEventFieldValue.FromLong(durationMs.Value);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            evt.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(error);
        }

        return evt;
    }
}

internal sealed class AgUiResearchStreamEventSinkFactory : IResearchingStreamEventSinkFactory
{
    private readonly IResearchingRuntime _runtime;

    public AgUiResearchStreamEventSinkFactory(IResearchingRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public IResearchStreamEventSink? Create(ResearchSession session, string runId)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("runId is required", nameof(runId));

        return new AgUiResearchStreamEventSink(
            _runtime,
            sessionId: session.Id,
            hub: session.Events,
            threadId: session.Id,
            runId: runId);
    }
}
