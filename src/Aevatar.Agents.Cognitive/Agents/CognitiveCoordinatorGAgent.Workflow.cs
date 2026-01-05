using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Utilities;
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
    [EventHandler]
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

            await PublishAsync(new WorkflowCompletedEventProto
            {
                ExecutionId = CustomState.ExecutionId,
                Success = true,
                Result = _workflowVariables.GetValueOrDefault("_output")?.ToString() ?? ""
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WORKFLOW] Execution failed: {Message}", ex.Message);
            await FailExecutionAsync(ex.Message);
        }
    }

    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        Logger.LogInformation("[DEBUG][Workflow] Executing workflow '{Name}' with {Count} steps: [{Steps}]",
            workflow.Name, workflow.Steps.Count, string.Join(", ", workflow.Steps.Select(s => s.Id)));

        for (int stepIndex = 0; stepIndex < workflow.Steps.Count; stepIndex++)
        {
            var step = workflow.Steps[stepIndex];
            Logger.LogInformation("[DEBUG][Workflow] >>> Executing step {Index}/{Total}: '{StepId}' (type={Type})",
                stepIndex + 1, workflow.Steps.Count, step.Id, step.Type);

            CustomState.CurrentPhase = $"Step: {step.Id}";
            CustomState.CurrentStepId = step.Id;

            var result = await ExecuteStepAsync(step);

            if (!result.Success)
            {
                throw new Exception($"Step '{step.Id}' failed: {result.Error}");
            }

            // Store result
            if (!string.IsNullOrEmpty(step.Store))
            {
                Logger.LogInformation(
                    "[DEBUG][Workflow] Step '{StepId}' store='{Store}', result.Value is null: {IsNull}",
                    step.Id, step.Store, result.Value == null);

                if (result.Value != null)
                {
                    _workflowVariables[step.Store] = result.Value;
                    Logger.LogInformation("[DEBUG][Workflow] Stored '{Store}' type: {Type}",
                        step.Store, result.Value.GetType().FullName);
                }
                else
                {
                    Logger.LogWarning("[DEBUG][Workflow] ⚠️ Step '{StepId}' returned null, NOT storing to '{Store}'",
                        step.Id, step.Store);
                }
            }
        }

        // Build output
        _workflowVariables["_output"] = BuildOutput(workflow.Output);
    }

    private async Task FailExecutionAsync(string error)
    {
        CustomState.Status = ExecutionStatus.EsFailed;
        CustomState.CurrentPhase = "Failed";
        CustomState.Error = error;

        // IMPORTANT:
        // - Previously no logging here, causing illusion of "backend didn't error but system stopped"
        // - Failures must be visible in logs (at least include executionId / current step)
        Logger.LogError("[WORKFLOW] Failed (executionId={ExecutionId}, step={StepId}, phase={Phase}): {Error}",
            CustomState.ExecutionId, CustomState.CurrentStepId, CustomState.CurrentPhase, error);

        await PublishAsync(new WorkflowCompletedEventProto
        {
            ExecutionId = CustomState.ExecutionId,
            Success = false,
            Error = error
        });
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

