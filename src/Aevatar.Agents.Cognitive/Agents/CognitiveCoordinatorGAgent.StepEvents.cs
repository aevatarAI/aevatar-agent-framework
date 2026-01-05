using Aevatar.Agents.Cognitive.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Step Events (UI/Observability)
//
//  WHY:
//  - Separate "execution engine" and "visualization events" to avoid giant file getting out of control.
//  - Events must be thread-safe: vote streaming / fan_out will trigger callbacks concurrently.
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// Set step event callback (for real-time visualization)
    /// </summary>
    public void SetStepEventCallback(Action<WorkflowStepEvent> callback)
    {
        _onStepEvent = callback;
    }

    /// <summary>
    /// Get all step events (for replay)
    /// </summary>
    public IReadOnlyList<WorkflowStepEvent> GetStepEvents()
    {
        // NOTE:
        // - vote / fan_out may concurrently write _stepEvents
        // - Directly exposing List will cause reading end to throw exception or read torn data when enumerating
        lock (_stepEventsLock)
        {
            return _stepEvents.ToList();
        }
    }

    private void EmitStepEvent(
        StepDefinition step,
        StepStatus status,
        string? message = null,
        float progress = 0,
        int voteRound = 0,
        int voteMaxRounds = 0,
        int voteK = 0,
        int voteCurrentVotes = 0,
        int parallelTotal = 0,
        int parallelCompleted = 0,
        int parallelFailed = 0,
        string? parentStepId = null,
        // LLM conversation history
        string? systemPrompt = null,
        string? userPrompt = null,
        string? assistantResponse = null,
        // Red-Flag information
        string? redFlagReason = null)
    {
        var now = DateTime.UtcNow;
        var durationMs = 0;

        if (status == StepStatus.Running)
        {
            _stepStartTimes[step.Id] = now;
        }
        else if (_stepStartTimes.TryGetValue(step.Id, out var startTime))
        {
            durationMs = (int)(now - startTime).TotalMilliseconds;
        }

        var evt = new WorkflowStepEvent
        {
            RunId = CustomState.ExecutionId ?? "",
            WorkflowName = CustomState.WorkflowName ?? "",
            StepId = step.Id,
            StepType = step.Type,
            Status = status,
            Progress = progress,
            Message = message ?? GetDefaultMessage(step, status),
            Timestamp = Timestamp.FromDateTime(now),
            ParentStepId = parentStepId ?? "",
            Depth = CustomState.CurrentDepth,
            VoteRound = voteRound,
            VoteMaxRounds = voteMaxRounds,
            VoteK = voteK,
            VoteCurrentVotes = voteCurrentVotes,
            ParallelTotal = parallelTotal,
            ParallelCompleted = parallelCompleted,
            ParallelFailed = parallelFailed,
            DurationMs = durationMs,
            LlmCalls = CustomState.TotalLlmCalls,
            TokensUsed = CustomState.TotalTokensUsed,
            // LLM conversation history
            SystemPrompt = systemPrompt ?? "",
            UserPrompt = userPrompt ?? "",
            AssistantResponse = assistantResponse ?? "",
            // Red-Flag information
            RedFlagReason = redFlagReason ?? ""
        };

        // When multiple tasks parallel (vote streaming), avoid List concurrent writes causing memory corruption/hang
        lock (_stepEventsLock)
        {
            _stepEvents.Add(evt);
        }
        _onStepEvent?.Invoke(evt);

        Logger.LogDebug("[Workflow] Step {StepId} ({Type}): {Status} - {Message}",
            step.Id, step.Type, status, message);
    }

    private static string GetDefaultMessage(StepDefinition step, StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => $"Step '{step.Id}' pending",
            StepStatus.Running => $"Executing {step.Type}: {step.Id}",
            StepStatus.Completed => $"Step '{step.Id}' completed",
            StepStatus.Failed => $"Step '{step.Id}' failed",
            StepStatus.Skipped => $"Step '{step.Id}' skipped",
            _ => $"Step '{step.Id}' - {status}"
        };
    }
}

