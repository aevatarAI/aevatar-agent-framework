using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Workflow lifecycle
//
//  WHY:
//  - Extract "start/failure/output building/main loop" from giant file.
//  - Make core execution logic easier to reuse as AevatarKit's Run Orchestrator.
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// Directly start workflow execution (API call)
    /// </summary>
    public Task StartWorkflowAsync(string workflowName, Dictionary<string, object>? variables = null)
    {
        var request = new StartWorkflowRequestEvent { WorkflowName = workflowName };
        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }
        }

        return HandleStartWorkflowRequest(request);
    }

    /// <summary>
    /// Start workflow execution (Protobuf event)
    /// </summary>
    public async Task HandleStartWorkflowRequest(StartWorkflowRequestEvent request)
    {
        Logger.LogInformation("Coordinator {Id} starting workflow: {WorkflowName}",
            Id, request.WorkflowName);

        CustomState.ExecutionId = Guid.NewGuid().ToString("N")[..16];
        CustomState.WorkflowName = request.WorkflowName;
        CustomState.Status = ExecutionStatus.EsRunning;
        CustomState.CurrentPhase = "Starting";

        try
        {
            if (string.IsNullOrWhiteSpace(SessionId))
            {
                await EmitSessionTraceAsync(
                    ExecutionTraceEventPhase.SessionStart,
                    ExecutionTraceEventStatus.Running,
                    error: null);
            }

            // Get workflow definition
            var workflow = _workflowRegistry.Get(request.WorkflowName);
            if (workflow == null)
            {
                await FailExecutionAsync($"Workflow '{request.WorkflowName}' not found");
                return;
            }

            // Inject initial variables
            _workflowVariables.Clear();

            // 1. Apply inputs' default values first
            foreach (var input in workflow.Inputs)
            {
                if (input.DefaultValue != null)
                {
                    _workflowVariables[input.Name] = input.DefaultValue;
                }
            }

            // 2. Override with passed variables (passed variables have higher priority)
            foreach (var (key, value) in request.Variables)
            {
                _workflowVariables[key] = ProtoValueConverter.FromProto(value);
            }

            // #region agent log
            var inputNames = new List<string>();
            foreach (var input in workflow.Inputs)
            {
                if (!string.IsNullOrWhiteSpace(input.Name))
                    inputNames.Add(input.Name);
            }
            _workflowVariables.TryGetValue("attachments", out var attachmentsValue);
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = SessionId ?? string.Empty,
                    runId = CustomState.ExecutionId ?? string.Empty,
                    hypothesisId = "H3",
                    location = "CognitiveCoordinatorGAgent.Workflow.cs:HandleStartWorkflow",
                    message = "workflow_vars_ready",
                    data = new
                    {
                        workflowName = request.WorkflowName,
                        inputNames,
                        variablesKeys = _workflowVariables.Keys,
                        hasAttachmentsVar = attachmentsValue != null,
                        attachmentsType = attachmentsValue?.GetType().Name ?? "missing"
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion

            // ============================================================
            //  Input-driven MaxDepth (eliminate hardcoding)
            //
            //  Convention:
            //  - Use `max_depth` uniformly as workflow recursion depth control input
            //  - If not provided, keep OnActivateAsync default value or YAML input default value
            // ============================================================
            if (TryGetPositiveInt(_workflowVariables, "max_depth", out var inputMaxDepth))
            {
                // Safety valve: avoid configuration errors causing extreme depth to crash system
                CustomState.MaxDepth = Math.Clamp(inputMaxDepth, 1, 200);
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                var taskLen = _workflowVariables.GetValueOrDefault("task")?.ToString()?.Length ?? 0;
                Logger.LogDebug(
                    "[Workflow Start] Variables: [{Vars}], task length={TaskLen}",
                    string.Join(", ", _workflowVariables.Keys),
                    taskLen);
            }

            // Execute workflow
            await ExecuteWorkflowAsync(workflow);

            // Complete
            CustomState.Status = ExecutionStatus.EsCompleted;
            CustomState.CurrentPhase = "Completed";

            await TryExportExecutionTraceAsync();

            await PublishAsync(new WorkflowCompletedEventProto
            {
                ExecutionId = CustomState.ExecutionId,
                Success = true,
                Result = _workflowVariables.GetValueOrDefault("_output")?.ToString() ?? ""
            });

            await EmitSessionTraceAsync(
                ExecutionTraceEventPhase.SessionStop,
                ExecutionTraceEventStatus.Completed,
                error: null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WORKFLOW] Execution failed: {Message}", ex.Message);
            await FailExecutionAsync(ex.Message);
        }
    }

    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        var orchestrator = new WorkflowOrchestrator(
            logger: Logger,
            variables: _workflowVariables,
            executeStep: ExecuteStepAsync,
            beforeStep: step =>
            {
                CustomState.CurrentPhase = $"Step: {step.Id}";
                CustomState.CurrentStepId = step.Id;
            },
            buildOutput: BuildOutput);

        await orchestrator.ExecuteAsync(workflow);
    }

    private async Task FailExecutionAsync(string error)
    {
        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = SessionId ?? string.Empty,
                runId = CustomState.ExecutionId ?? string.Empty,
                hypothesisId = "H6",
                location = "CognitiveCoordinatorGAgent.Workflow.cs:FailExecutionAsync",
                message = "workflow_failed",
                data = new
                {
                    error,
                    stepId = CustomState.CurrentStepId ?? string.Empty,
                    phase = CustomState.CurrentPhase ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        CustomState.Status = ExecutionStatus.EsFailed;
        CustomState.CurrentPhase = "Failed";
        CustomState.Error = error;

        // IMPORTANT:
        // - Previously no logging here, causing illusion of "backend didn't error but system stopped"
        // - Failures must be visible in logs (at least include executionId / current step)
        Logger.LogError("[WORKFLOW] Failed (executionId={ExecutionId}, step={StepId}, phase={Phase}): {Error}",
            CustomState.ExecutionId, CustomState.CurrentStepId, CustomState.CurrentPhase, error);

        await EmitSessionTraceAsync(
            ExecutionTraceEventPhase.SessionStop,
            ExecutionTraceEventStatus.Failed,
            error);

        await TryExportExecutionTraceAsync();

        await PublishAsync(new WorkflowCompletedEventProto
        {
            ExecutionId = CustomState.ExecutionId,
            Success = false,
            Error = error
        });
    }

    private async Task TryExportExecutionTraceAsync()
    {
        var store = ExecutionTraceStore;
        if (store == null)
            return;

        try
        {
            var executionId = CustomState.ExecutionId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(executionId))
            {
                // Last resort: stable-ish id for export directory.
                executionId = Id;
            }

            SyncStatsSnapshot();
            ExecutionTrace trace;
            var stepEvents = GetStepEvents();
            if (stepEvents.Count > 0)
            {
                trace = stepEvents.ToExecutionTrace();
            }
            else
            {
                // No step events collected (failed early). Emit minimal trace.
                var now = DateTime.UtcNow;
                trace = new ExecutionTrace
                {
                    ExecutionId = executionId,
                    Kind = ExecutionTraceKind.Workflow,
                    Status = MapStatus(CustomState.Status),
                    Name = CustomState.WorkflowName ?? "workflow",
                    Description = "Cognitive workflow execution (no step events captured)",
                    StartedAt = Timestamp.FromDateTime(now),
                    EndedAt = Timestamp.FromDateTime(now),
                    Cost = new ExecutionTraceCost
                    {
                        DurationMs = 0,
                        TotalLlmCalls = CustomState.TotalLlmCalls,
                        TotalTokens = CustomState.TotalTokensUsed
                    },
                    Root = new ExecutionTraceNode
                    {
                        NodeId = executionId,
                        Name = CustomState.WorkflowName ?? "workflow",
                        Type = "workflow",
                        Status = MapStatus(CustomState.Status),
                        Error = CustomState.Error ?? string.Empty
                    },
                    Error = CustomState.Error ?? string.Empty
                };
            }

            // Ensure export id is always present (FileExecutionTraceStore requires it).
            if (string.IsNullOrWhiteSpace(trace.ExecutionId))
            {
                trace.ExecutionId = executionId;
            }

            // Align status/error with coordinator state (single truth).
            trace.Status = MapStatus(CustomState.Status);
            trace.Error = CustomState.Error ?? trace.Error;

            // Labels for indexing/debugging.
            trace.Labels["cognitive.workflow_name"] = CustomState.WorkflowName ?? string.Empty;
            trace.Labels["cognitive.coordinator_id"] = Id;
            trace.Labels["cognitive.status"] = CustomState.Status.ToString();
            if (!string.IsNullOrWhiteSpace(CustomState.CurrentStepId))
            {
                trace.Labels["cognitive.current_step_id"] = CustomState.CurrentStepId;
            }

            await store.SaveAsync(trace, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[WORKFLOW] Failed to export ExecutionTrace (executionId={ExecutionId})",
                CustomState.ExecutionId);
        }
    }

    private async Task EmitSessionTraceAsync(
        string phase,
        string status,
        string? error)
    {
        var sessionId = SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        var evt = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = phase,
            Message = phase,
            NodeId = $"session:{sessionId}"
        };

        evt.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(status);
        evt.Fields[ExecutionTraceEventFields.Phase] =
            ExecutionTraceEventFieldValue.FromString(phase);
        evt.Fields[ExecutionTraceEventFields.SessionId] =
            ExecutionTraceEventFieldValue.FromString(sessionId);
        evt.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(CustomState.ExecutionId ?? string.Empty);
        evt.Fields[ExecutionTraceEventFields.WorkflowName] =
            ExecutionTraceEventFieldValue.FromString(CustomState.WorkflowName ?? string.Empty);
        evt.Fields[ExecutionTraceEventFields.AgentId] =
            ExecutionTraceEventFieldValue.FromString(Id);

        if (!string.IsNullOrWhiteSpace(error))
        {
            evt.Fields[ExecutionTraceEventFields.Error] =
                ExecutionTraceEventFieldValue.FromString(error);
        }

        try
        {
            await PublishAsync(evt);
        }
        catch
        {
            // best-effort only
        }
    }

    private static ExecutionTraceStatus MapStatus(ExecutionStatus status)
    {
        return status switch
        {
            ExecutionStatus.EsCompleted => ExecutionTraceStatus.Succeeded,
            ExecutionStatus.EsFailed => ExecutionTraceStatus.Failed,
            ExecutionStatus.EsCancelled => ExecutionTraceStatus.Cancelled,
            ExecutionStatus.EsRunning => ExecutionTraceStatus.Running,
            ExecutionStatus.EsPending => ExecutionTraceStatus.Running,
            _ => ExecutionTraceStatus.Unspecified
        };
    }

    private object BuildOutput(Dictionary<string, string> outputDef)
    {
        if (outputDef.Count == 0)
            return _workflowVariables;

        var output = new Dictionary<string, object?>();
        foreach (var (key, template) in outputDef)
        {
            output[key] = _templateEngine.ResolveValue(template, _workflowVariables);
        }

        return output;
    }
}

