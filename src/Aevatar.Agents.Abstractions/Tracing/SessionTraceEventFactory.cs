using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions.Tracing;

public static class SessionTraceEventFactory
{
    public static ExecutionTraceEvent CreateSessionStart(
        string sessionId,
        string? executionId = null,
        string? agentId = null,
        string? workflowName = null)
    {
        return CreateSessionEvent(
            phase: ExecutionTraceEventPhase.SessionStart,
            status: ExecutionTraceEventStatus.Running,
            sessionId: sessionId,
            executionId: executionId,
            agentId: agentId,
            workflowName: workflowName,
            error: null,
            durationMs: null);
    }

    public static ExecutionTraceEvent CreateSessionStop(
        string sessionId,
        string status = ExecutionTraceEventStatus.Completed,
        string? executionId = null,
        string? agentId = null,
        string? workflowName = null,
        string? error = null,
        long? durationMs = null)
    {
        return CreateSessionEvent(
            phase: ExecutionTraceEventPhase.SessionStop,
            status: string.IsNullOrWhiteSpace(status) ? ExecutionTraceEventStatus.Completed : status,
            sessionId: sessionId,
            executionId: executionId,
            agentId: agentId,
            workflowName: workflowName,
            error: error,
            durationMs: durationMs);
    }

    private static ExecutionTraceEvent CreateSessionEvent(
        string phase,
        string status,
        string sessionId,
        string? executionId,
        string? agentId,
        string? workflowName,
        string? error,
        long? durationMs)
    {
        var cleanSessionId = (sessionId ?? string.Empty).Trim();
        if (cleanSessionId.Length == 0)
            throw new ArgumentException("sessionId is required.", nameof(sessionId));

        var cleanExecutionId = (executionId ?? string.Empty).Trim();
        if (cleanExecutionId.Length == 0)
            cleanExecutionId = cleanSessionId;

        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = phase,
            NodeId = $"session:{cleanSessionId}"
        };

        evt.Fields[ExecutionTraceEventFields.Phase] =
            ExecutionTraceEventFieldValue.FromString(phase);
        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(status);
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString(cleanSessionId);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(cleanExecutionId);

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            evt.Fields[ExecutionTraceEventFields.AgentId] =
                ExecutionTraceEventFieldValue.FromString(agentId);
        }

        if (!string.IsNullOrWhiteSpace(workflowName))
        {
            evt.Fields[ExecutionTraceEventFields.WorkflowName] =
                ExecutionTraceEventFieldValue.FromString(workflowName);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            evt.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(error);
        }

        if (durationMs.HasValue)
        {
            evt.Fields[ExecutionTraceEventFields.DurationMs] =
                ExecutionTraceEventFieldValue.FromLong(durationMs.Value);
        }

        return evt;
    }
}
