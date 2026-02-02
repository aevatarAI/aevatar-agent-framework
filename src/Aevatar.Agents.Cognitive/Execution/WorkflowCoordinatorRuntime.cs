using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using PrimitiveResult = Aevatar.Agents.Cognitive.Primitives.PrimitiveResult;
using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class WorkflowCoordinatorRuntime : IWorkflowCoordinatorRuntime
{
    private readonly RoleAIGAgent _agent;
    private readonly ILogger _logger;
    private readonly Func<IMessage, EventDirection, CancellationToken, Task> _publish;
    private readonly WorkflowParser _workflowParser = new();
    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();
    private readonly CognitiveStepExecutionHandler _stepExecutionHandler = new();
    private readonly object _textStreamLock = new();
    private readonly HashSet<string> _textStreamStarted = new(StringComparer.Ordinal);

    private readonly Dictionary<string, object> _workflowVariables = new(StringComparer.Ordinal);
    private CognitiveStepExecutor? _stepExecutor;

    private string _executionId = string.Empty;
    private string _workflowName = string.Empty;
    private int _maxDepth = 50;
    private int _currentDepth;

    private TransformExecutor? _transformExecutor;
    private RetrieveFactsExecutor? _retrieveFactsExecutor;
    private WorkspaceReadFileExecutor? _workspaceReadFileExecutor;
    private WorkspaceCodeSearchExecutor? _workspaceCodeSearchExecutor;
    private WorkspaceApplyPatchExecutor? _workspaceApplyPatchExecutor;
    private SandboxCommandExecutor? _sandboxCommandExecutor;

    public WorkflowCoordinatorRuntime(
        RoleAIGAgent agent,
        ILogger logger,
        Func<IMessage, EventDirection, CancellationToken, Task> publish)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _stepExecutionHandler.OnStreamingDelta = (request, delta) => EmitStepDelta(request, delta);
    }

    public Dictionary<string, object> WorkflowVariables => _workflowVariables;
    public ILogger Logger => _logger;
    public TemplateEngine TemplateEngine => _templateEngine;

    public bool TryGetWorkflowVariable(string key, out object? value)
        => _workflowVariables.TryGetValue(key, out value);

    public Task HandleStepCompletedEvent(StepCompletedEventProto evt)
    {
        // Sequential runtime: no worker pool, so no aggregation needed.
        return Task.CompletedTask;
    }

    public async Task HandleStartWorkflowRequest(StartWorkflowRequestEvent request)
    {
        if (request == null)
            return;

        _executionId = Guid.NewGuid().ToString("N")[..16];
        _workflowName = (request.WorkflowName ?? string.Empty).Trim();
        if (_workflowName.Length == 0)
            _workflowName = "workflow";

        _workflowVariables.Clear();
        foreach (var (key, value) in request.Variables)
        {
            _workflowVariables[key] = ProtoValueConverter.FromProto(value);
        }

        var runId = ResolveRunIdFromVariables();
        if (!string.IsNullOrWhiteSpace(runId))
            _executionId = runId;

        if (!_workflowVariables.ContainsKey("workflow_name"))
            _workflowVariables["workflow_name"] = _workflowName;

        if (_workflowVariables.TryGetValue("max_depth", out var depthObj))
        {
            _maxDepth = ConvertToInt(depthObj, _maxDepth);
            _maxDepth = Math.Clamp(_maxDepth, 1, 200);
        }

        var workflow = ResolveWorkflowDefinition(_workflowName);
        if (workflow == null)
        {
            await PublishCompletedAsync(false, "workflow_not_found");
            return;
        }

        if (!string.IsNullOrWhiteSpace(workflow.Name))
            _workflowName = workflow.Name.Trim();

        try
        {
            await ExecuteWorkflowAsync(workflow);
            await PublishCompletedAsync(true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow execution failed: {Workflow}", _workflowName);
            await PublishCompletedAsync(false, ex.Message);
        }
    }

    private WorkflowDefinition? ResolveWorkflowDefinition(string workflowName)
    {
        if (_workflowVariables.TryGetValue("workflow_yaml", out var yamlObj) &&
            yamlObj is string yaml && !string.IsNullOrWhiteSpace(yaml))
        {
            return _workflowParser.Parse(yaml);
        }

        if (_workflowVariables.TryGetValue("workflow_path", out var pathObj) &&
            pathObj is string path && !string.IsNullOrWhiteSpace(path))
        {
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);
                return _workflowParser.Parse(raw);
            }

            var baseDir = Path.GetDirectoryName(path) ?? string.Empty;
            if (baseDir.Length > 0)
            {
                var yamlPath = Path.Combine(baseDir, $"{workflowName}.yaml");
                var ymlPath = Path.Combine(baseDir, $"{workflowName}.yml");
                var selected = File.Exists(yamlPath) ? yamlPath : File.Exists(ymlPath) ? ymlPath : string.Empty;
                if (selected.Length > 0)
                {
                    var raw = File.ReadAllText(selected);
                    return _workflowParser.Parse(raw);
                }
            }
        }

        return null;
    }

    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        _stepExecutor = CognitiveStepExecutorFactory.CreateForCoordinator(this, SnapshotStepModules());
        var orchestrator = new WorkflowOrchestrator(
            _logger,
            _workflowVariables,
            ExecuteStepAsync,
            beforeStep: null,
            buildOutput: BuildOutput);
        await orchestrator.ExecuteAsync(workflow);
    }

    private ICognitiveStepModule[] SnapshotStepModules()
    {
        var modules = _agent.GetEventModules();
        if (modules.Count == 0)
            return Array.Empty<ICognitiveStepModule>();

        var list = new List<ICognitiveStepModule>();
        foreach (var module in modules)
        {
            if (module is ICognitiveStepModule stepModule)
                list.Add(stepModule);
        }

        return list.ToArray();
    }

    private async Task<PrimitiveResult> ExecuteStepAsync(StepDefinition step)
    {
        if (_stepExecutor == null)
            _stepExecutor = CognitiveStepExecutorFactory.CreateForCoordinator(this, SnapshotStepModules());

        return await _stepExecutor.ExecuteAsync(step);
    }

    private object BuildOutput(Dictionary<string, string> output)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expr) in output)
        {
            result[key] = _templateEngine.ResolveValue(expr, _workflowVariables);
        }
        return result;
    }

    private async Task PublishCompletedAsync(bool success, string error)
    {
        var evt = new WorkflowCompletedEventProto
        {
            ExecutionId = _executionId,
            Success = success,
            Error = error ?? string.Empty,
            Result = success && _workflowVariables.TryGetValue("_output", out var val) && val != null
                ? val.ToString() ?? string.Empty
                : string.Empty
        };
        await _publish(evt, EventDirection.Down, CancellationToken.None);
    }

    // ============================================================
    //  Step events (minimal)
    // ============================================================
    public void EmitStepStart(StepDefinition step, string? userPrompt, string? systemPrompt)
        => PublishStepEvent(step, StepStatus.Running, "running", userPrompt, systemPrompt, null);

    private void EmitStepDelta(ExecuteStepRequestEvent request, string delta)
    {
        if (string.IsNullOrWhiteSpace(delta))
            return;

        var stepId = (request.StepId ?? string.Empty).Trim();
        if (stepId.Length == 0)
            stepId = (request.StepType ?? "assistant").Trim();

        var msgId = BuildTextMessageId(stepId);
        if (string.IsNullOrWhiteSpace(msgId))
            return;

        var ts = Timestamp.FromDateTime(DateTime.UtcNow);
        if (TryStartTextStream(msgId))
        {
            _ = _publish(new TextMessageStartEvent
            {
                Timestamp = ts,
                MessageId = msgId,
                Role = "assistant"
            }, EventDirection.Down, CancellationToken.None);
        }

        _ = _publish(new TextMessageContentEvent
        {
            Timestamp = ts,
            MessageId = msgId,
            Delta = delta
        }, EventDirection.Down, CancellationToken.None);
    }

    private bool TryStartTextStream(string msgId)
    {
        lock (_textStreamLock)
        {
            if (_textStreamStarted.Contains(msgId))
                return false;
            _textStreamStarted.Add(msgId);
            return true;
        }
    }

    private bool TryEndTextStream(string msgId)
    {
        lock (_textStreamLock)
        {
            return _textStreamStarted.Remove(msgId);
        }
    }

    private string BuildTextMessageId(string stepId)
    {
        var sessionId = ResolveSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
            return string.Empty;
        var agentKey = string.IsNullOrWhiteSpace(stepId) ? "assistant" : stepId.Trim();
        var runId = string.IsNullOrWhiteSpace(_executionId) ? "run" : _executionId;
        return $"msg:{sessionId}:{agentKey}:{runId}";
    }

    public void EmitStepCompleted(StepDefinition step, PrimitiveResult result)
    {
        var assistant = result.AssistantResponse;
        if (string.IsNullOrWhiteSpace(assistant) && result.Value is string text && !string.IsNullOrWhiteSpace(text))
        {
            assistant = text;
        }

        var sessionId = ResolveSessionId();
        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId,
                runId = _executionId ?? string.Empty,
                hypothesisId = "H2",
                location = "WorkflowCoordinatorRuntime.cs:EmitStepCompleted",
                message = "emit_step_completed",
                data = new
                {
                    stepId = step.Id ?? string.Empty,
                    stepType = step.Type ?? string.Empty,
                    success = result.Success,
                    assistantLen = assistant?.Length ?? 0,
                    hasValue = result.Value != null,
                    valueType = result.Value?.GetType().Name ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        PublishStepEvent(
            step,
            result.Success ? StepStatus.Completed : StepStatus.Failed,
            result.Success ? "completed" : result.Error ?? "failed",
            result.UserPrompt,
            result.SystemPrompt,
            assistant);
    }

    public void EmitStepError(StepDefinition step, Exception ex)
        => PublishStepEvent(step, StepStatus.Failed, ex.Message, null, null, null);

    private void PublishStepEvent(
        StepDefinition step,
        StepStatus status,
        string message,
        string? userPrompt,
        string? systemPrompt,
        string? assistantResponse)
    {
        var now = DateTime.UtcNow;
        var evt = new WorkflowStepEvent
        {
            RunId = _executionId ?? string.Empty,
            WorkflowName = _workflowName ?? string.Empty,
            StepId = step.Id ?? string.Empty,
            StepType = step.Type ?? string.Empty,
            Status = status,
            Message = message ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(now)
        };

        if (!string.IsNullOrWhiteSpace(userPrompt))
            evt.UserPrompt = userPrompt;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            evt.SystemPrompt = systemPrompt;
        if (!string.IsNullOrWhiteSpace(assistantResponse))
            evt.AssistantResponse = assistantResponse;

        var sessionId = ResolveSessionId();
        if (status == StepStatus.Completed || status == StepStatus.Failed)
        {
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId = evt.RunId ?? string.Empty,
                    hypothesisId = "H2",
                    location = "WorkflowCoordinatorRuntime.cs:PublishStepEvent",
                    message = "publish_step_event",
                    data = new
                    {
                        stepId = evt.StepId ?? string.Empty,
                        stepType = evt.StepType ?? string.Empty,
                        status = evt.Status.ToString(),
                        assistantLen = evt.AssistantResponse?.Length ?? 0
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion

            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId = evt.RunId ?? string.Empty,
                    hypothesisId = "H71",
                    location = "WorkflowCoordinatorRuntime.cs:PublishStepEvent",
                    message = "trace_publish_target",
                    data = new
                    {
                        agentId = _agent.Id,
                        stepId = evt.StepId ?? string.Empty,
                        stepType = evt.StepType ?? string.Empty,
                        assistantLen = evt.AssistantResponse?.Length ?? 0
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }

        _ = _publish(evt, EventDirection.Down, CancellationToken.None);
        if (status == StepStatus.Completed || status == StepStatus.Failed)
        {
            var msgId = BuildTextMessageId(step.Id ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(msgId) && TryEndTextStream(msgId))
            {
                _ = _publish(new TextMessageEndEvent
                {
                    Timestamp = evt.Timestamp,
                    MessageId = msgId
                }, EventDirection.Down, CancellationToken.None);
            }
        }

        var traceEvent = BuildExecutionTraceEvent(
            evt,
            message ?? string.Empty,
            ResolveSessionId(),
            _agent.Id);
        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            JsonSerializer.Serialize(new
            {
                sessionId,
                runId = evt.RunId ?? string.Empty,
                hypothesisId = "H5",
                location = "WorkflowCoordinatorRuntime.cs:PublishStepEvent",
                message = "trace_publish_scheduled",
                data = new
                {
                    stepId = evt.StepId ?? string.Empty,
                    stepType = evt.StepType ?? string.Empty,
                    assistantLen = evt.AssistantResponse?.Length ?? 0
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        var traceTask = _publish(traceEvent, EventDirection.Down, CancellationToken.None);
        _ = traceTask.ContinueWith(task =>
        {
            if (!task.IsFaulted)
                return;
            var error = task.Exception?.GetBaseException().Message ?? "unknown";
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId = evt.RunId ?? string.Empty,
                    hypothesisId = "H5",
                    location = "WorkflowCoordinatorRuntime.cs:PublishStepEvent",
                    message = "trace_publish_failed",
                    data = new
                    {
                        stepId = evt.StepId ?? string.Empty,
                        stepType = evt.StepType ?? string.Empty,
                        error
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }, TaskScheduler.Default);
    }

    private string ResolveSessionId()
    {
        if (_workflowVariables.TryGetValue("session_id", out var value) && value != null)
            return value.ToString() ?? string.Empty;
        if (_workflowVariables.TryGetValue("sessionId", out var camel) && camel != null)
            return camel.ToString() ?? string.Empty;
        return string.Empty;
    }

    private string ResolveRunIdFromVariables()
    {
        if (_workflowVariables.TryGetValue("run_id", out var runId) && runId != null)
            return runId.ToString() ?? string.Empty;
        if (_workflowVariables.TryGetValue("request_id", out var requestId) && requestId != null)
            return requestId.ToString() ?? string.Empty;
        return string.Empty;
    }

    private ExecutionTraceEvent BuildExecutionTraceEvent(
        WorkflowStepEvent evt,
        string resolvedMessage,
        string sessionId,
        string agentId)
    {
        var traceEvent = new ExecutionTraceEvent
        {
            Timestamp = evt.Timestamp,
            Phase = evt.StepType ?? string.Empty,
            Message = resolvedMessage,
            NodeId = evt.StepId ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            traceEvent.Fields[ExecutionTraceEventFields.SessionId] =
                ExecutionTraceEventFieldValue.FromString(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            traceEvent.Fields[ExecutionTraceEventFields.AgentId] =
                ExecutionTraceEventFieldValue.FromString(agentId);
        }
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var agentKey = (evt.StepId ?? string.Empty).Trim();
            if (agentKey.Length == 0)
                agentKey = "assistant";
            var runKey = (evt.RunId ?? string.Empty).Trim();
            if (runKey.Length == 0)
                runKey = "run";
            traceEvent.Fields[ExecutionTraceEventFields.MessageId] =
                ExecutionTraceEventFieldValue.FromString($"msg:{sessionId}:{agentKey}:{runKey}");
        }

        traceEvent.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(MapTraceStatus(evt.Status));
        traceEvent.Fields[ExecutionTraceEventFields.Progress] =
            ExecutionTraceEventFieldValue.FromDouble(evt.Progress);
        traceEvent.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(evt.RunId);
        traceEvent.Fields[ExecutionTraceEventFields.WorkflowName] =
            ExecutionTraceEventFieldValue.FromString(evt.WorkflowName);
        traceEvent.Fields[ExecutionTraceEventFields.StepType] =
            ExecutionTraceEventFieldValue.FromString(evt.StepType);
        traceEvent.Fields[ExecutionTraceEventFields.Depth] =
            ExecutionTraceEventFieldValue.FromInt(evt.Depth);
        traceEvent.Fields[ExecutionTraceEventFields.DurationMs] =
            ExecutionTraceEventFieldValue.FromInt(evt.DurationMs);
        traceEvent.Fields[ExecutionTraceEventFields.TokensUsed] =
            ExecutionTraceEventFieldValue.FromInt(evt.TokensUsed);
        traceEvent.Fields[ExecutionTraceEventFields.LlmCalls] =
            ExecutionTraceEventFieldValue.FromInt(evt.LlmCalls);

        if (!string.IsNullOrWhiteSpace(evt.ParentStepId))
        {
            traceEvent.Fields[ExecutionTraceEventFields.ParentStepId] =
                ExecutionTraceEventFieldValue.FromString(evt.ParentStepId);
        }

        if (!string.IsNullOrWhiteSpace(evt.UserPrompt))
        {
            traceEvent.Fields[ExecutionTraceEventFields.UserPrompt] =
                ExecutionTraceEventFieldValue.FromString(evt.UserPrompt);
        }

        if (!string.IsNullOrWhiteSpace(evt.SystemPrompt))
        {
            traceEvent.Fields[ExecutionTraceEventFields.SystemPrompt] =
                ExecutionTraceEventFieldValue.FromString(evt.SystemPrompt);
        }

        if (!string.IsNullOrWhiteSpace(evt.AssistantResponse))
        {
            traceEvent.Fields[ExecutionTraceEventFields.AssistantResponse] =
                ExecutionTraceEventFieldValue.FromString(evt.AssistantResponse);
            traceEvent.Fields[ExecutionTraceEventFields.Phase] =
                ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventPhase.LlmResponse);
        }
        else if (evt.Status == StepStatus.Failed && !string.IsNullOrWhiteSpace(resolvedMessage))
        {
            var errorText = $"[error] {resolvedMessage}".Trim();
            traceEvent.Fields[ExecutionTraceEventFields.AssistantResponse] =
                ExecutionTraceEventFieldValue.FromString(errorText);
            traceEvent.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(resolvedMessage);
            traceEvent.Fields[ExecutionTraceEventFields.Phase] =
                ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventPhase.LlmResponse);
        }

        return traceEvent;
    }

    private static string MapTraceStatus(StepStatus status)
        => status switch
        {
            StepStatus.Pending => ExecutionTraceEventStatus.Pending,
            StepStatus.Running => ExecutionTraceEventStatus.Running,
            StepStatus.Completed => ExecutionTraceEventStatus.Completed,
            StepStatus.Failed => ExecutionTraceEventStatus.Failed,
            _ => ExecutionTraceEventStatus.Running
        };

    // ============================================================
    //  Built-in step handlers
    // ============================================================
    public async Task<PrimitiveResult> ExecuteLlmCallDirectAsync(
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem)
    {
        return await ExecuteStepWithHandlerAsync(step, "llm_call", preRenderedPrompt, preRenderedSystem);
    }

    public async Task<PrimitiveResult> ExecuteToolCallAsync(StepDefinition step)
        => await ExecuteStepWithHandlerAsync(step, "tool_call", null, null);

    public async Task<PrimitiveResult> ExecuteToolValidateAsync(StepDefinition step)
        => await ExecuteStepWithHandlerAsync(step, "tool_validate", null, null);

    public Task<PrimitiveResult> ExecuteToolEvolveAsync(StepDefinition step)
        => Task.FromResult(PrimitiveResult.Fail("tool_evolve not supported in yaml runtime"));

    private async Task<PrimitiveResult> ExecuteStepWithHandlerAsync(
        StepDefinition step,
        string stepType,
        string? preRenderedPrompt,
        string? preRenderedSystem)
    {
        var request = new ExecuteStepRequestEvent
        {
            RequestId = $"{_executionId}-{step.Id}",
            StepId = step.Id ?? string.Empty,
            StepType = stepType
        };

        foreach (var (key, value) in step.Parameters)
        {
            if (string.Equals(key, "prompt", StringComparison.OrdinalIgnoreCase) && preRenderedPrompt != null)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(preRenderedPrompt);
                continue;
            }

            if (string.Equals(key, "system", StringComparison.OrdinalIgnoreCase) && preRenderedSystem != null)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(preRenderedSystem);
                continue;
            }

            request.Parameters[key] = ProtoValueConverter.ToProto(value);
        }

        foreach (var (key, value) in _workflowVariables)
        {
            request.Variables[key] = ProtoValueConverter.ToProto(value);
        }

        var envelope = new EventEnvelope { Payload = Any.Pack(request) };
        var result = await _stepExecutionHandler.HandleAsync(envelope, _agent, CancellationToken.None);
        if (result?.Response is not StepCompletedEventProto completed)
        {
            return PrimitiveResult.Fail("step_execution_failed");
        }

        if (!completed.Success)
            return PrimitiveResult.Fail(completed.Error ?? "step_failed");

        var outputType = step.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        var strictParse = ResolveBoolParameter(step.Parameters, "strict_parse", true);

        object? parsed = completed.Result ?? string.Empty;
        if (!string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
        {
            parsed = ParseOutput(completed.Result ?? string.Empty, outputType);
            if (parsed == null && !strictParse)
                parsed = completed.Result ?? string.Empty;
        }

        return new PrimitiveResult
        {
            Success = true,
            Value = parsed,
            AssistantResponse = completed.Result ?? string.Empty,
            TokensUsed = completed.TokensUsed,
            LlmCalls = completed.LlmCalls
        };
    }

    public async Task<PrimitiveResult> ExecuteConditionalAsync(StepDefinition step)
    {
        var conditionExpr = step.Condition ?? "false";
        object? conditionResult;
        try
        {
            conditionResult = _templateEngine.Evaluate(conditionExpr, _workflowVariables);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Conditional] Evaluate failed at step {StepId}: {Expr}", step.Id, conditionExpr);
            conditionResult = false;
        }

        var isTrue = conditionResult switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "yes",
            int i => i != 0,
            double d => d != 0,
            _ => conditionResult != null
        };

        var branch = isTrue ? step.IfTrue : step.IfFalse;
        if (branch == null || branch.Count == 0)
            return PrimitiveResult.Ok();

        PrimitiveResult? last = null;
        foreach (var childStep in branch)
        {
            last = await ExecuteStepAsync(childStep);
            if (!last.Success)
                break;

            if (!string.IsNullOrEmpty(childStep.Store) && last.Value != null)
                _workflowVariables[childStep.Store] = last.Value;
        }

        return last ?? PrimitiveResult.Ok();
    }

    public async Task<PrimitiveResult> ExecuteFanOutAsync(StepDefinition step)
    {
        var forEach = step.ForEach ?? step.Parameters.GetValueOrDefault("for_each")?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(forEach))
            return PrimitiveResult.Fail("fan_out requires for_each");

        if (!TryGetWorkflowVariable(forEach, out var itemsObj) || itemsObj == null)
            return PrimitiveResult.Fail($"fan_out variable '{forEach}' not found");

        if (step.Step == null)
            return PrimitiveResult.Fail("fan_out requires child step");

        var items = ConvertToList(itemsObj);
        return await ExecuteFanOutSequentialAsync(step, items, step.Step);
    }

    public async Task<PrimitiveResult> ExecuteParallelAsync(StepDefinition step)
    {
        var steps = step.Parameters.GetValueOrDefault("steps") as List<StepDefinition>;
        if (steps == null || steps.Count == 0)
            return PrimitiveResult.Ok(new Dictionary<string, object?>());

        var outputs = new Dictionary<string, object?>();
        var totalTokens = 0;
        var totalCalls = 0;

        foreach (var childStep in steps)
        {
            var result = await ExecuteStepAsync(childStep);
            totalTokens += result.TokensUsed;
            totalCalls += result.LlmCalls;

            if (!string.IsNullOrEmpty(childStep.Store) && result.Value != null)
            {
                outputs[childStep.Store] = result.Value;
                _workflowVariables[childStep.Store] = result.Value;
            }
        }

        return PrimitiveResult.Ok(outputs, totalTokens, totalCalls);
    }

    public async Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step)
    {
        var generator = step.Generator;
        if (generator == null)
            return PrimitiveResult.Fail("vote requires generator");

        // 简化：顺序生成一次提案并返回 (保留 YAML 兼容性)
        return await ExecuteLlmCallDirectAsync(generator, null, null);
    }

    public async Task<PrimitiveResult> ExecuteWorkflowCallAsync(StepDefinition step)
    {
        var rawName = step.Workflow ?? string.Empty;
        var workflowName = _templateEngine.Render(rawName, _workflowVariables).Trim();
        if (workflowName.Length == 0)
            return PrimitiveResult.Fail("workflow_call requires workflow name");

        if (_currentDepth >= _maxDepth)
            return PrimitiveResult.Fail($"Max recursion depth {_maxDepth} exceeded");

        var workflow = ResolveWorkflowDefinition(workflowName);
        if (workflow == null || !string.Equals(workflow.Name, workflowName, StringComparison.OrdinalIgnoreCase))
            return PrimitiveResult.Fail($"Workflow '{workflowName}' not found");

        _currentDepth++;
        var saved = new Dictionary<string, object>(_workflowVariables);
        try
        {
            var snapshot = new Dictionary<string, object>(_workflowVariables);
            if (step.Params != null)
            {
                foreach (var (key, value) in step.Params)
                {
                    if (value == null) continue;
                    snapshot[key] = _templateEngine.ResolveValue(value, _workflowVariables);
                }
            }

            _workflowVariables.Clear();
            foreach (var (k, v) in snapshot)
                _workflowVariables[k] = v;

            await ExecuteWorkflowAsync(workflow);
            return _workflowVariables.TryGetValue("_output", out var output) && output != null
                ? PrimitiveResult.Ok(output)
                : PrimitiveResult.Ok();
        }
        finally
        {
            _workflowVariables.Clear();
            foreach (var (k, v) in saved)
                _workflowVariables[k] = v;
            _currentDepth--;
        }
    }

    public Task<PrimitiveResult> ExecuteCheckpointAsync(StepDefinition step)
    {
        var names = step.Parameters.GetValueOrDefault("vars") as List<string> ?? new List<string>();
        var snapshot = new Dictionary<string, object?>();
        foreach (var name in names)
        {
            if (_workflowVariables.TryGetValue(name, out var val))
                snapshot[name] = val;
        }

        return Task.FromResult(PrimitiveResult.Ok(snapshot));
    }

    public Task<PrimitiveResult> ExecuteAssignAsync(StepDefinition step)
    {
        var from = step.Parameters.GetValueOrDefault("from")?.ToString();
        if (string.IsNullOrWhiteSpace(from))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'from'"));

        if (string.IsNullOrWhiteSpace(step.Store))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'store'"));

        var value = ResolvePathValue(_workflowVariables, from);
        if (value == null)
            return Task.FromResult(PrimitiveResult.Fail($"assign source '{from}' resolved to null"));

        _workflowVariables[step.Store!] = value;
        return Task.FromResult(PrimitiveResult.Ok(value));
    }

    public Task<PrimitiveResult> ExecuteTransformAsync(StepDefinition step)
    {
        _transformExecutor ??= new TransformExecutor(_templateEngine, _logger);
        var result = _transformExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    public Task<PrimitiveResult> ExecuteRetrieveFactsAsync(StepDefinition step)
    {
        _retrieveFactsExecutor ??= new RetrieveFactsExecutor(_templateEngine, _logger);
        var result = _retrieveFactsExecutor.Execute(step, _workflowVariables);
        return Task.FromResult(result);
    }

    public Task<PrimitiveResult> ExecuteWorkspaceReadFileAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceReadFileExecutor ??= new WorkspaceReadFileExecutor(_templateEngine, _logger);
        var result = _workspaceReadFileExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    public Task<PrimitiveResult> ExecuteWorkspaceCodeSearchAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceCodeSearchExecutor ??= new WorkspaceCodeSearchExecutor(_templateEngine, _logger);
        var result = _workspaceCodeSearchExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    public Task<PrimitiveResult> ExecuteWorkspaceApplyPatchAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _workspaceApplyPatchExecutor ??= new WorkspaceApplyPatchExecutor(_templateEngine, _logger);
        var result = _workspaceApplyPatchExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    public Task<PrimitiveResult> ExecuteSandboxCommandAsync(StepDefinition step)
    {
        if (!WorkspacePathGuard.TryGetWorkspaceRoot(out var root, out var rootError))
            return Task.FromResult(PrimitiveResult.Fail(rootError!));

        _sandboxCommandExecutor ??= new SandboxCommandExecutor(_templateEngine, _logger);
        var result = _sandboxCommandExecutor.Execute(step, _workflowVariables, root!);
        return Task.FromResult(result);
    }

    private async Task<PrimitiveResult> ExecuteFanOutSequentialAsync(
        StepDefinition step,
        List<object> items,
        StepDefinition childStep)
    {
        var includeFailures = ResolveBoolParameter(step.Parameters, "include_failures", false);
        var results = new List<object>();
        var totalTokens = 0;
        var totalCalls = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _workflowVariables["item"] = item;
            _workflowVariables["index"] = i;

            var result = await ExecuteStepAsync(childStep);
            totalTokens += result.TokensUsed;
            totalCalls += result.LlmCalls;

            if (result.Success && result.Value != null)
            {
                results.Add(result.Value);
            }
            else if (includeFailures)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["worker_id"] = "coordinator",
                    ["step_id"] = childStep.Id,
                    ["success"] = false,
                    ["error"] = result.Error ?? "failed"
                });
            }
        }

        _workflowVariables.Remove("item");
        _workflowVariables.Remove("index");

        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);
        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }

    private object ApplyReducer(List<object> results, string reducer)
    {
        return reducer switch
        {
            "flatten" => results.SelectMany(FlattenResult).ToList(),
            _ => results
        };
    }

    private static IEnumerable<object> FlattenResult(object value)
    {
        if (value is IEnumerable<object> list)
            return list;
        if (value is System.Collections.IEnumerable enumerable)
            return enumerable.Cast<object>();
        return new[] { value };
    }

    private object? ParseOutput(string content, string outputType)
    {
        var parser = _parserFactory.Create(outputType);
        try
        {
            return parser.Parse(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ParseOutput failed for type {Type}", outputType);
            return null;
        }
    }

    private static object? ResolvePathValue(Dictionary<string, object> variables, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!variables.TryGetValue(parts[0], out var current) || current == null)
            return null;

        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            current = current switch
            {
                IDictionary<string, object> dict => dict.TryGetValue(key, out var v) ? v : null,
                System.Collections.IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (current == null) return null;
        }

        return current;
    }

    private static List<object> ConvertToList(object items)
    {
        return items switch
        {
            IEnumerable<object> enumerable => enumerable.ToList(),
            System.Collections.IList list => list.Cast<object>().ToList(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
            _ => [items]
        };
    }

    private static int ConvertToInt(object? value, int fallback)
    {
        if (value == null) return fallback;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed) => parsed,
            _ => fallback
        };
    }

    private bool ResolveBoolParameter(Dictionary<string, object?> parameters, string key, bool defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.True => true,
            JsonElement el when el.ValueKind == JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
