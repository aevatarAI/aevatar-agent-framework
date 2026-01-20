using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Progress Reporter
//  Handles progress reporting and red flag management
//  Part of MakerCoordinatorGAgent (partial class)
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  Progress Reporting
    // ============================================================

    /// <summary>
    /// Report progress to callback and publish event.
    /// </summary>
    private void ReportProgress(MakerProgress progress)
    {
        _progressCallback?.Invoke(progress);

        _ = PublishAsync(new MakerProgressEvent
        {
            ExecutionId = CustomState.ExecutionId,
            TaskId = progress.TaskId,
            Phase = progress.Phase.ToString().ToLowerInvariant(),
            Message = progress.Message,
            Depth = progress.Depth,
            Timestamp = Timestamp.FromDateTimeOffset(progress.Timestamp),
            VotingRound = progress.Voting?.Round ?? 0,
            VotingTotal = progress.Voting?.TotalVotes ?? 0,
            VotingLeader = progress.Voting?.LeaderVotes ?? 0,
            VotingRunnerUp = progress.Voting?.RunnerUpVotes ?? 0,
            VotingNeeded = progress.Voting?.VotesNeeded ?? 0,
            ProposalId = progress.Proposal?.ProposalId ?? string.Empty,
            ProposalContent = progress.Proposal?.Content ?? string.Empty,
            ProposalSuccess = progress.Proposal?.Success ?? false
        });

        var traceEvent = BuildExecutionTraceEvent(progress);
        _ = PublishAsync(traceEvent);
    }

    private ExecutionTraceEvent BuildExecutionTraceEvent(MakerProgress progress)
    {
        var phase = progress.Phase.ToString().ToLowerInvariant();
        var traceEvent = new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTimeOffset(progress.Timestamp),
            Phase = phase,
            Message = progress.Message ?? string.Empty,
            NodeId = progress.TaskId ?? string.Empty
        };

        traceEvent.Fields[ExecutionTraceEventFields.Status] =
            ExecutionTraceEventFieldValue.FromString(MapTraceStatus(progress.Phase));
        traceEvent.Fields[ExecutionTraceEventFields.Progress] =
            ExecutionTraceEventFieldValue.FromDouble(GetPhaseProgress(progress.Phase));
        traceEvent.Fields[ExecutionTraceEventFields.ExecutionId] =
            ExecutionTraceEventFieldValue.FromString(CustomState.ExecutionId ?? string.Empty);
        traceEvent.Fields[ExecutionTraceEventFields.WorkflowName] =
            ExecutionTraceEventFieldValue.FromString("maker");
        traceEvent.Fields[ExecutionTraceEventFields.StepType] =
            ExecutionTraceEventFieldValue.FromString(phase);
        traceEvent.Fields[ExecutionTraceEventFields.Depth] =
            ExecutionTraceEventFieldValue.FromInt(progress.Depth);

        var totalLlmCalls = GetTotalLlmCalls();
        if (totalLlmCalls > 0)
        {
            traceEvent.Fields[ExecutionTraceEventFields.LlmCalls] =
                ExecutionTraceEventFieldValue.FromInt(totalLlmCalls);
        }

        if (CustomState.TotalTokensUsed > 0)
        {
            traceEvent.Fields[ExecutionTraceEventFields.TokensUsed] =
                ExecutionTraceEventFieldValue.FromLong(CustomState.TotalTokensUsed);
        }

        if (CustomState.TotalPromptTokens > 0)
        {
            traceEvent.Fields[ExecutionTraceEventFields.PromptTokens] =
                ExecutionTraceEventFieldValue.FromLong(CustomState.TotalPromptTokens);
        }

        if (CustomState.TotalCompletionTokens > 0)
        {
            traceEvent.Fields[ExecutionTraceEventFields.CompletionTokens] =
                ExecutionTraceEventFieldValue.FromLong(CustomState.TotalCompletionTokens);
        }

        if (progress.Voting != null)
        {
            traceEvent.Fields[ExecutionTraceEventMakerFields.VoteRound] =
                ExecutionTraceEventFieldValue.FromInt(progress.Voting.Round);
            traceEvent.Fields[ExecutionTraceEventMakerFields.VoteK] =
                ExecutionTraceEventFieldValue.FromInt(progress.Voting.VotesNeeded);
            traceEvent.Fields[ExecutionTraceEventMakerFields.VoteCurrentVotes] =
                ExecutionTraceEventFieldValue.FromInt(progress.Voting.LeaderVotes);
        }

        if (progress.Proposal != null)
        {
            traceEvent.Fields[ExecutionTraceEventMakerFields.ProposalId] =
                ExecutionTraceEventFieldValue.FromString(progress.Proposal.ProposalId);

            if (!string.IsNullOrWhiteSpace(progress.Proposal.WorkerId))
            {
                traceEvent.Fields[ExecutionTraceEventMakerFields.WorkerId] =
                    ExecutionTraceEventFieldValue.FromString(progress.Proposal.WorkerId);
            }
        }

        if (progress.StreamingToken != null)
        {
            traceEvent.Fields[ExecutionTraceEventMakerFields.WorkerId] =
                ExecutionTraceEventFieldValue.FromString(progress.StreamingToken.WorkerId);
            traceEvent.Fields[ExecutionTraceEventMakerFields.ProposalId] =
                ExecutionTraceEventFieldValue.FromString(progress.StreamingToken.ProposalId);

            if (progress.StreamingToken.IsLastToken &&
                !string.IsNullOrWhiteSpace(progress.StreamingToken.AccumulatedContent))
            {
                traceEvent.Fields[ExecutionTraceEventFields.AssistantResponse] =
                    ExecutionTraceEventFieldValue.FromString(progress.StreamingToken.AccumulatedContent);
            }
        }

        return traceEvent;
    }

    private static string MapTraceStatus(MakerPhase phase)
    {
        return phase switch
        {
            MakerPhase.Starting => ExecutionTraceEventStatus.Pending,
            MakerPhase.Completed => ExecutionTraceEventStatus.Completed,
            MakerPhase.Failed => ExecutionTraceEventStatus.Failed,
            _ => ExecutionTraceEventStatus.Running
        };
    }

    private static double GetPhaseProgress(MakerPhase phase)
    {
        return phase switch
        {
            MakerPhase.Completed => 1.0,
            MakerPhase.Failed => 1.0,
            _ => 0.0
        };
    }

    // ============================================================
    //  Red Flag Management
    // ============================================================

    /// <summary>
    /// Add a red flag event for tracking issues during execution.
    /// </summary>
    private void AddRedFlag(string taskId, string reason)
    {
        _redFlags.Add(new RedFlagEvent
        {
            TaskId = taskId,
            Reason = reason,
            Recovered = false
        });
        CustomState.RedFlagReasons.Add($"{taskId}: {reason}");
    }
}

