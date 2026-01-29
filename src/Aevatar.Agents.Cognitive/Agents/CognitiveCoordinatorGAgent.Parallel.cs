using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Utilities;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Parallel execution (fan_out / parallel)
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// Handle Worker completion event (Protobuf event)
    /// </summary>
    public Task HandleStepCompletedEvent(StepCompletedEventProto evt)
    {
        Logger.LogDebug("Coordinator received step completed: {StepId} from {WorkerId}",
            evt.StepId, evt.WorkerId);

        // fan_out completion semantics:
        // - streaming intermediate state: Success=false && Error="" (not counted as completion)
        // - terminal completion: Success=true or Success=false && Error!= "" (failure also counts as "completion", otherwise fan_out would meaninglessly hang)
        var isStreaming = !evt.Success && string.IsNullOrEmpty(evt.Error);
        var isTerminal = evt.Success || (!evt.Success && !string.IsNullOrEmpty(evt.Error));

        // Update statistics (only count terminal events, avoid repeated accumulation of streaming intermediate states causing explosion)
        if (isTerminal)
        {
            AddStats(evt.TokensUsed, evt.LlmCalls);
        }

        // Collect results
        _collectedResults[evt.RequestId] = evt;

        _fanOutChildTypes.TryGetValue(evt.RequestId, out var childType);
        if (isTerminal)
        {
            _fanOutChildTypes.TryRemove(evt.RequestId, out _);
            // prompts are only for UI; safe to release after terminal
            _fanOutUserPrompts.TryRemove(evt.RequestId, out _);
            _fanOutSystemPrompts.TryRemove(evt.RequestId, out _);
        }

        // Subtask completion event (for frontend parallel visualization)
        if (!string.IsNullOrEmpty(evt.StepId) && !string.IsNullOrEmpty(childType))
        {
            var childDef = new StepDefinition
            {
                Id = evt.StepId,
                Type = childType
            };

            // UI semantics:
            // - streaming: Running
            // - terminal fail: Failed
            // - terminal success: Completed
            var status = evt.Success ? StepStatus.Completed : (isStreaming ? StepStatus.Running : StepStatus.Failed);
            var userPrompt = _fanOutUserPrompts.TryGetValue(evt.RequestId, out var up) ? up : "";
            var systemPrompt = _fanOutSystemPrompts.TryGetValue(evt.RequestId, out var sp) ? sp : "";
            EmitStepEvent(childDef,
                status,
                evt.Success
                    ? "Subtask completed"
                    : (string.IsNullOrEmpty(evt.Error) ? "Subtask streaming" : $"Subtask failed: {evt.Error}"),
                progress: evt.Success ? 1.0f : 0.0f,
                parentStepId: _currentFanOutStep?.Id,
                assistantResponse: evt.Result,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);
        }

        // Send parallel progress event
        if (_currentFanOutStep != null)
        {
            var succeeded = _collectedResults.Values.Count(r => r.Success);
            var failed = _collectedResults.Values.Count(r => !r.Success && !string.IsNullOrEmpty(r.Error));
            var terminal = _collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error));

            EmitStepEvent(_currentFanOutStep, StepStatus.Running,
                $"Progress: {terminal}/{_expectedResults} (ok: {succeeded}, failed: {failed})",
                progress: (float)terminal / _expectedResults,
                parallelTotal: _expectedResults,
                // parallelCompleted means "terminal completion count" (success + failure), avoid failure causing never reaching full
                parallelCompleted: terminal,
                parallelFailed: failed);
        }

        // Check if all results collected
        // Terminal completion (success or failure) both count as "collected"
        if (_collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error)) >= _expectedResults)
        {
            _currentFanOutStep = null; // Clear current fan-out step
            _fanOutCompletionSource?.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    // ============================================================
    //  Parallel Steps - Distribute to Workers (True Actor Parallelism)
    // ============================================================

    internal async Task<PrimitiveResult> ExecuteFanOutAsync(StepDefinition step)
    {
        // Get iteration list
        var forEachVar = step.ForEach ?? "";
        if (!_workflowVariables.TryGetValue(forEachVar, out var itemsObj))
        {
            return PrimitiveResult.Fail($"Variable '{forEachVar}' not found");
        }

        var items = ConvertToList(itemsObj);
        if (items.Count == 0)
        {
            return PrimitiveResult.Ok(new List<object>());
        }

        var childStep = step.Step;
        if (childStep == null)
        {
            return PrimitiveResult.Fail("fan_out requires 'step' definition");
        }

        // workflow_call type needs Coordinator to handle itself (recursion), cannot distribute to Worker
        // Worker only supports llm_call
        var isWorkflowCall = childStep.Type == "workflow_call";

        // Check if Workers available (only llm_call gets distributed)
        if (_workerIds.Count == 0 || isWorkflowCall)
        {
            if (isWorkflowCall)
            {
                Logger.LogInformation("Fan-out contains workflow_call, executing in Coordinator (parallel Tasks)");
            }
            else
            {
                Logger.LogWarning("No workers available, falling back to sequential execution");
            }

            return await ExecuteFanOutSequentialAsync(step, items, childStep);
        }

        Logger.LogInformation("Fan-out executing {Count} items across {Workers} workers",
            items.Count, _workerIds.Count);

        // Prepare to collect results
        _collectedResults.Clear();
        _expectedResults = items.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();
        _currentFanOutStep = step;
        _fanOutChildTypes.Clear();
        _fanOutUserPrompts.Clear();
        _fanOutSystemPrompts.Clear();
        _fanOutOutputTypes.Clear();

        // Send fan-out start event
        EmitStepEvent(step, StepStatus.Running,
            $"Distributing {items.Count} tasks to {_workerIds.Count} workers",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        // Distribute tasks to Workers (true Actor parallelism)
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var targetWorker = _workerIds[i % _workerIds.Count];

            // Build child context
            var childVariables = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i,
                // Ensure exactly-one worker executes this task (fan_out uses Down broadcast).
                // Worker will ignore if not matching its own Id.
                ["__target_worker"] = targetWorker
            };

            // Create Protobuf request event
            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-{step.Id}-{i}",
                StepId = $"{step.Id}[{i}]",
                StepType = childStep.Type
            };

            // Convert parameters and variables to Protobuf
            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in childVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }

            _fanOutChildTypes[request.RequestId] = childStep.Type;

            // Keep the declared output type so Coordinator can parse worker raw results deterministically.
            // NOTE: We intentionally do NOT remove this mapping on terminal events, because ExecuteFanOutAsync
            //       needs it after fan_out completes to parse and reduce results.
            _fanOutOutputTypes[request.RequestId] =
                childStep.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

            // Pre-render subtask prompt for frontend display
            var renderedPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var renderedSystem = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            renderedPrompt = _templateEngine.Render(renderedPrompt, childVariables);
            if (renderedSystem != null)
            {
                renderedSystem = _templateEngine.Render(renderedSystem, childVariables);
            }

            _fanOutUserPrompts[request.RequestId] = renderedPrompt;
            if (renderedSystem != null)
            {
                _fanOutSystemPrompts[request.RequestId] = renderedSystem;
            }

            // Send start event for subtask (for frontend parallel visualization)
            var childDef = new StepDefinition
            {
                Id = request.StepId,
                Type = childStep.Type
            };
            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: renderedSystem,
                userPrompt: renderedPrompt);

            // Send to Workers (downward broadcast, all Children will receive)
            // Workers determine whether to process based on RequestId
            await PublishAsync(request, EventDirection.Down);
        }

        // Wait for all Workers to complete (event-driven, non-blocking wait)
        var timeoutSeconds = ResolveIntParameter(step.Parameters, "timeout_seconds", 600);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var completed = await Task.WhenAny(
            _fanOutCompletionSource.Task,
            Task.Delay(timeout));

        if (completed != _fanOutCompletionSource.Task)
        {
            return PrimitiveResult.Fail($"Fan-out timed out after {timeout}");
        }

        // Collect and parse results:
        // - Worker returns RAW assistant response in StepCompletedEventProto.Result (string)
        // - Coordinator parses it according to the declared output type (json/json_array/first_line/...)
        // - Optionally keep failures as items (include_failures=true) to enable deterministic aggregation
        var includeFailures = ResolveBoolParameter(step.Parameters, "include_failures", false);

        var results = new List<object>();
        foreach (var r in _collectedResults.Values.OrderBy(r => r.StepId))
        {
            // Determine output type (default: text)
            var outputType = _fanOutOutputTypes.TryGetValue(r.RequestId, out var ot) ? ot : "text";
            outputType = string.IsNullOrWhiteSpace(outputType) ? "text" : outputType;

            if (r.Success)
            {
                object? parsed;
                if (string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    // Preserve legacy behavior for text fan_out (no trimming/parsing side effects).
                    parsed = r.Result;
                }
                else
                {
                    parsed = ParseOutput(r.Result ?? "", outputType);
                }

                if (parsed != null)
                {
                    results.Add(parsed);
                }
                else if (includeFailures)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["worker_id"] = r.WorkerId,
                        ["step_id"] = r.StepId,
                        ["success"] = false,
                        ["error"] = "redflag-parse-null"
                    });
                }
            }
            else if (includeFailures)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["worker_id"] = r.WorkerId,
                    ["step_id"] = r.StepId,
                    ["success"] = false,
                    ["error"] = r.Error
                });
            }
        }

        // Aggregate
        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);

        var totalTokens = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls = _collectedResults.Values.Sum(r => r.LlmCalls);

        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }

    /// <summary>
    /// Coordinator internal sequential execution (for workflow_call or no Worker scenarios)
    /// 
    /// ⚠️ Orleans compatibility:
    /// - Grain is turn-based single-threaded, cannot use Task.Run
    /// - State modifications must be within Grain thread
    /// - True parallelism requires distributing to Worker Grains
    /// </summary>
    private async Task<PrimitiveResult> ExecuteFanOutSequentialAsync(
        StepDefinition step, List<object> items, StepDefinition childStep)
    {
        // Send fan-out start event
        EmitStepEvent(step, StepStatus.Running,
            $"Executing {items.Count} subtasks (sequential in Coordinator)",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        var includeFailures = ResolveBoolParameter(step.Parameters, "include_failures", false);
        var results = new List<object>();
        var totalTokens = 0;
        var totalCalls = 0;

        // Sequentially execute each subtask (Orleans compatible)
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // Send start event for subtask
            var childDef = new StepDefinition { Id = $"{step.Id}[{i}]", Type = childStep.Type };

            // Pre-render prompt (using temporary variables)
            var tempVars = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i
            };
            var userPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString();
            var systemPrompt = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            if (userPrompt != null) userPrompt = _templateEngine.Render(userPrompt, tempVars);
            if (systemPrompt != null) systemPrompt = _templateEngine.Render(systemPrompt, tempVars);

            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);

            // Temporarily set variables
            _workflowVariables["item"] = item;
            _workflowVariables["index"] = i;

            try
            {
                var result = await ExecuteStepAsync(childStep);

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // Send subtask completion event
                EmitStepEvent(childDef, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Subtask {i + 1} completed" : $"Subtask {i + 1} failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    assistantResponse: result.AssistantResponse);

                // DEBUG: Check subtask result
                Logger.LogInformation(
                    "[DEBUG][FanOut] Subtask {Index} result: Success={Success}, Value is null={IsNull}, Value type={Type}",
                    i, result.Success, result.Value == null, result.Value?.GetType().FullName ?? "null");

                if (result.Success && result.Value != null)
                {
                    results.Add(result.Value);
                    Logger.LogInformation("[DEBUG][FanOut] Added subtask {Index} result to collection, total: {Count}",
                        i, results.Count);
                }
                else if (result.Success && result.Value == null)
                {
                    Logger.LogWarning(
                        "[DEBUG][FanOut] ⚠️ Subtask {Index} succeeded but Value is null, NOT added to results!", i);
                    if (includeFailures)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            ["worker_id"] = "coordinator",
                            ["step_id"] = childDef.Id,
                            ["success"] = false,
                            ["error"] = "redflag-parse-null"
                        });
                    }
                }
                else if (!result.Success && includeFailures)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["worker_id"] = "coordinator",
                        ["step_id"] = childDef.Id,
                        ["success"] = false,
                        ["error"] = result.Error ?? ""
                    });
                }

                // Send progress event
                EmitStepEvent(step, StepStatus.Running,
                    $"Progress: {i + 1}/{items.Count} subtasks completed",
                    progress: (float)(i + 1) / items.Count,
                    parallelTotal: items.Count, parallelCompleted: i + 1);
            }
            finally
            {
                // Clean up temporary variables
                _workflowVariables.Remove("item");
                _workflowVariables.Remove("index");
            }
        }

        // Send fan-out completion event
        EmitStepEvent(step, StepStatus.Completed,
            $"All {items.Count} subtasks completed",
            progress: 1,
            parallelTotal: items.Count, parallelCompleted: results.Count);

        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);

        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }

    internal async Task<PrimitiveResult> ExecuteParallelAsync(StepDefinition step)
    {
        var steps = step.Parameters.GetValueOrDefault("steps") as List<StepDefinition>;
        if (steps == null || steps.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>());
        }

        // If no Workers, execute sequentially
        if (_workerIds.Count == 0)
        {
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

        // Parallel execution when Workers available
        _collectedResults.Clear();
        _expectedResults = steps.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();

        for (int i = 0; i < steps.Count; i++)
        {
            var childStep = steps[i];

            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-parallel-{i}",
                StepId = childStep.Id,
                StepType = childStep.Type
            };

            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in _workflowVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }

            await PublishAsync(request, EventDirection.Down);
        }

        // Wait for completion
        await _fanOutCompletionSource.Task;

        // Aggregate results
        var outputs2 = new Dictionary<string, object?>();
        foreach (var result in _collectedResults.Values.Where(r => r.Success))
        {
            var originalStep = steps.FirstOrDefault(s => s.Id == result.StepId);
            if (originalStep?.Store != null)
            {
                var outputType = originalStep.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
                var strictParse = ResolveBoolParameter(originalStep.Parameters, "strict_parse", true);

                object? parsed;
                if (string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = result.Result;
                }
                else
                {
                    parsed = ParseOutput(result.Result ?? "", outputType);
                    if (parsed == null && !strictParse)
                    {
                        parsed = result.Result;
                    }
                }

                if (parsed != null)
                {
                    outputs2[originalStep.Store] = parsed;
                    _workflowVariables[originalStep.Store] = parsed;
                }
            }
        }

        var totalTokens2 = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls2 = _collectedResults.Values.Sum(r => r.LlmCalls);

        return PrimitiveResult.Ok(outputs2, totalTokens2, totalCalls2);
    }
}

