using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Primitives;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Execution.Run;

public interface IWorkflowRunEventSink
{
    void Publish(AgUiEvent evt);
    void SetMessage(string messageId, string role, string content);
    void AppendToMessage(string messageId, string role, string delta);
}

internal sealed class NullWorkflowRunEventSink : IWorkflowRunEventSink
{
    public static readonly NullWorkflowRunEventSink Instance = new();

    public void Publish(AgUiEvent evt) { }
    public void SetMessage(string messageId, string role, string content) { }
    public void AppendToMessage(string messageId, string role, string delta) { }
}

public interface IWorkflowRunEventEmitter
{
    void EmitRunStarted(WorkflowRunContext context);
    void EmitRunFinished(WorkflowRunContext context, WorkflowRunResult result);
    void EmitRunError(WorkflowRunContext context, Exception ex);
    void EmitStepStarted(WorkflowRunContext context, StepDefinition step);
    void EmitStepFinished(WorkflowRunContext context, StepDefinition step, PrimitiveResult result);
    void EmitStepError(WorkflowRunContext context, StepDefinition step, Exception ex);
}

public sealed class AgUiMessageEmitter
{
    private readonly IWorkflowRunEventSink _sink;

    public AgUiMessageEmitter(IWorkflowRunEventSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void EmitUserMessage(string messageId, string content)
    {
        var id = (messageId ?? string.Empty).Trim();
        if (id.Length == 0) return;

        StartMessage(id, role: "user");
        AppendDelta(id, role: "user", delta: content ?? string.Empty);
        EndMessage(id);
    }

    public void StartMessage(string messageId, string role)
    {
        var id = (messageId ?? string.Empty).Trim();
        if (id.Length == 0) return;
        role = (role ?? string.Empty).Trim();
        if (role.Length == 0) return;

        _sink.SetMessage(id, role, string.Empty);
        _sink.Publish(new TextMessageStartEvent
        {
            Timestamp = NowMs(),
            MessageId = id,
            Role = role
        });
    }

    public void AppendDelta(string messageId, string role, string delta)
    {
        var id = (messageId ?? string.Empty).Trim();
        if (id.Length == 0) return;
        role = (role ?? string.Empty).Trim();
        if (role.Length == 0) return;
        if (string.IsNullOrEmpty(delta)) return;

        _sink.AppendToMessage(id, role, delta);
        _sink.Publish(new TextMessageContentEvent
        {
            Timestamp = NowMs(),
            MessageId = id,
            Delta = delta
        });
    }

    public void EndMessage(string messageId)
    {
        var id = (messageId ?? string.Empty).Trim();
        if (id.Length == 0) return;

        _sink.Publish(new TextMessageEndEvent
        {
            Timestamp = NowMs(),
            MessageId = id
        });
    }

    public void PublishCustom(string name, object? value)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) return;

        _sink.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = n,
            Value = value
        });
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class AgUiRunEventEmitter : IWorkflowRunEventEmitter
{
    private readonly AgUiMessageEmitter _messages;

    public AgUiRunEventEmitter(IWorkflowRunEventSink sink)
    {
        _messages = new AgUiMessageEmitter(sink);
    }

    public void EmitRunStarted(WorkflowRunContext context)
    {
        var sink = context.Events;
        sink.Publish(new RunStartedEvent
        {
            Timestamp = NowMs(),
            ThreadId = context.ThreadId,
            RunId = context.RunId
        });

        if (context.Items.TryGetValue(WorkflowRunContextKeys.UserMessageText, out var userTextObj) &&
            userTextObj is string userText &&
            userText.Length > 0)
        {
            var userMessageId = ResolveMessageId(context, WorkflowRunContextKeys.UserMessageId, role: "user");
            _messages.EmitUserMessage(userMessageId, userText);
        }

        if (context.Items.TryGetValue(WorkflowRunContextKeys.AssistantMessageId, out var assistantIdObj) &&
            assistantIdObj is string assistantId &&
            assistantId.Length > 0)
        {
            var role = ResolveRole(context);
            _messages.StartMessage(assistantId, role);

            if (context.Items.TryGetValue(WorkflowRunContextKeys.AssistantMeta, out var metaObj) &&
                metaObj != null)
            {
                _messages.PublishCustom("aevatar.vibe.message_meta", metaObj);
            }
        }
    }

    public void EmitRunFinished(WorkflowRunContext context, WorkflowRunResult result)
    {
        if (context.Items.TryGetValue(WorkflowRunContextKeys.AssistantMessageId, out var assistantIdObj) &&
            assistantIdObj is string assistantId &&
            assistantId.Length > 0)
        {
            _messages.EndMessage(assistantId);
        }

        var payload = ResolveResultPayload(context, result);

        context.Events.Publish(new RunFinishedEvent
        {
            Timestamp = NowMs(),
            ThreadId = context.ThreadId,
            RunId = context.RunId,
            Result = payload
        });
    }

    public void EmitRunError(WorkflowRunContext context, Exception ex)
    {
        context.Events.Publish(new RunErrorEvent
        {
            Timestamp = NowMs(),
            Message = ex.Message,
            Code = "run_error"
        });
    }

    public void EmitStepStarted(WorkflowRunContext context, StepDefinition step)
    {
        context.Events.Publish(new StepStartedEvent
        {
            Timestamp = NowMs(),
            StepName = step.Id ?? step.Type ?? "step"
        });
    }

    public void EmitStepFinished(WorkflowRunContext context, StepDefinition step, PrimitiveResult result)
    {
        context.Events.Publish(new StepFinishedEvent
        {
            Timestamp = NowMs(),
            StepName = step.Id ?? step.Type ?? "step"
        });
    }

    public void EmitStepError(WorkflowRunContext context, StepDefinition step, Exception ex)
    {
        context.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.workflow.step_error",
            Value = new
            {
                runId = context.RunId,
                stepId = step.Id ?? string.Empty,
                stepType = step.Type ?? string.Empty,
                error = ex.Message
            }
        });
    }

    private static string ResolveMessageId(WorkflowRunContext context, string key, string role)
    {
        if (context.Items.TryGetValue(key, out var idObj) && idObj is string id && id.Length > 0)
            return id;
        return $"msg:{context.ThreadId}:{role}:{context.RunId}";
    }

    private static string ResolveRole(WorkflowRunContext context)
    {
        if (context.Items.TryGetValue(WorkflowRunContextKeys.AssistantRole, out var roleObj) &&
            roleObj is string role &&
            role.Length > 0)
        {
            return role;
        }
        return "assistant";
    }

    private static object ResolveResultPayload(WorkflowRunContext context, WorkflowRunResult result)
    {
        if (context.Items.TryGetValue(WorkflowRunContextKeys.RunResultPayloadBuilder, out var builderObj) &&
            builderObj is Func<WorkflowRunResult, object?> builder)
        {
            return builder(result) ?? new { ok = result.Success };
        }

        if (context.Items.TryGetValue(WorkflowRunContextKeys.RunResultPayload, out var payloadObj) &&
            payloadObj != null)
        {
            return payloadObj;
        }

        return result.ResultPayload ?? new
        {
            ok = result.Success,
            output = result.Output
        };
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class AgUiExecutionTraceEmitter
{
    private readonly IWorkflowRunEventSink _sink;
    private readonly Dictionary<string, string> _lastStatus = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public AgUiExecutionTraceEmitter(IWorkflowRunEventSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Emit(ExecutionTraceEvent evt, string? status = null, string? stepName = null)
    {
        var mapped = AgUiTraceProjector.Map(evt);
        if (mapped.Count == 0) return;

        var toPublish = new List<AgUiEvent>(mapped.Count);
        lock (_gate)
        {
            foreach (var e in mapped)
            {
                if (e is StepStartedEvent or StepFinishedEvent)
                {
                    if (!string.IsNullOrWhiteSpace(stepName) && !string.IsNullOrWhiteSpace(status))
                    {
                        if (_lastStatus.TryGetValue(stepName, out var prev) &&
                            string.Equals(prev, status, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        _lastStatus[stepName] = status!;
                    }
                }

                toPublish.Add(e);
            }
        }

        for (var i = 0; i < toPublish.Count; i++)
            _sink.Publish(toPublish[i]);
    }

    public static ExecutionTraceEvent BuildTraceEvent(
        string runId,
        string workflowName,
        string phase,
        string nodeId,
        string status,
        string? message,
        double? progress,
        string? stepType = null,
        int? depth = null,
        string? workerId = null)
    {
        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase ?? string.Empty,
            Message = message ?? string.Empty,
            NodeId = nodeId ?? string.Empty
        };

        evt.Fields[ExecutionTraceEventFields.Status] = ExecutionTraceEventFieldValue.FromString(status ?? string.Empty);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] = ExecutionTraceEventFieldValue.FromString(runId ?? string.Empty);
        evt.Fields[ExecutionTraceEventFields.WorkflowName] = ExecutionTraceEventFieldValue.FromString(workflowName ?? string.Empty);

        if (progress.HasValue)
            evt.Fields[ExecutionTraceEventFields.Progress] = ExecutionTraceEventFieldValue.FromDouble(progress.Value);
        if (!string.IsNullOrWhiteSpace(stepType))
            evt.Fields[ExecutionTraceEventFields.StepType] = ExecutionTraceEventFieldValue.FromString(stepType);
        if (depth.HasValue)
            evt.Fields[ExecutionTraceEventFields.Depth] = ExecutionTraceEventFieldValue.FromInt(depth.Value);
        if (!string.IsNullOrWhiteSpace(workerId))
            evt.Fields[ExecutionTraceEventMakerFields.WorkerId] = ExecutionTraceEventFieldValue.FromString(workerId);

        return evt;
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
