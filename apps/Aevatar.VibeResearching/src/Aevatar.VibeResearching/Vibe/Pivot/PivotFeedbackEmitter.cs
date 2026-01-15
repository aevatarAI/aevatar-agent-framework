using Aevatar.Agents.AGUI;
using Microsoft.Extensions.Logging;
using VibeResearching.Vibe.Pivot.Messages;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Emits AG-UI events for pivot operation feedback.
/// </summary>
public sealed class PivotFeedbackEmitter : IPivotFeedbackEmitter
{
    private readonly ILogger<PivotFeedbackEmitter> _logger;

    public PivotFeedbackEmitter(ILogger<PivotFeedbackEmitter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public PivotDetectedEvent CreateDetectedEvent(
        string sessionId,
        string pivotId,
        DirectionChangeIntent intent)
    {
        return new PivotDetectedEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            DetectedIntent = intent.NewTopic ?? PivotMessages.DetectionAcknowledgment_ZH,
            Confidence = intent.Confidence,
            NewTopic = intent.NewTopic,
            NeedsClarification = intent.NeedsClarification,
            PreserveAspects = intent.PreserveAspects
        };
    }

    /// <inheritdoc />
    public PivotStartedEvent CreateStartedEvent(
        string sessionId,
        string pivotId,
        string? oldDirection,
        string? newDirection)
    {
        return new PivotStartedEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            OldDirection = oldDirection,
            NewDirection = newDirection
        };
    }

    /// <inheritdoc />
    public PivotProgressEvent CreateProgressEvent(
        string sessionId,
        string pivotId,
        PivotProgressStage stage,
        int? count = null,
        double? progress = null,
        bool useChinese = true)
    {
        var message = stage switch
        {
            PivotProgressStage.CancellingPlans => PivotMessages.GetProgressCancellingPlans(count ?? 0, useChinese),
            PivotProgressStage.PreservingKnowledge => PivotMessages.GetProgressPreservingKnowledge(count ?? 0, useChinese),
            PivotProgressStage.NotifyingAgents => PivotMessages.GetProgressNotifyingAgents(useChinese),
            PivotProgressStage.UpdatingDag => PivotMessages.GetProgressUpdatingDag(useChinese),
            _ => stage.ToString()
        };

        return new PivotProgressEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            Stage = stage.ToString(),
            Message = message,
            Progress = progress
        };
    }

    /// <inheritdoc />
    public PivotCompletedEvent CreateCompletedEvent(
        string sessionId,
        string pivotId,
        PivotOperation operation,
        bool useChinese = true)
    {
        var summary = PivotMessages.GetCompletionSummary(
            operation.CancelledNodeIds.Count,
            operation.PreservedNodeIds.Count,
            useChinese);

        return new PivotCompletedEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            CancelledCount = operation.CancelledNodeIds.Count,
            PreservedCount = operation.PreservedNodeIds.Count,
            NewCount = operation.NewNodeIds.Count,
            DurationMs = operation.DurationMs,
            Summary = summary
        };
    }

    /// <inheritdoc />
    public PivotErrorEvent CreateErrorEvent(
        string sessionId,
        string pivotId,
        string errorMessage,
        string? errorCode = null)
    {
        return new PivotErrorEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }

    /// <inheritdoc />
    public PivotClarificationRequestEvent CreateClarificationRequestEvent(
        string sessionId,
        string pivotId,
        string? suggestedTopic,
        double confidence,
        string? oldTopic = null,
        bool useChinese = true)
    {
        var question = string.IsNullOrWhiteSpace(oldTopic) || string.IsNullOrWhiteSpace(suggestedTopic)
            ? PivotMessages.GetClarificationPrompt(useChinese)
            : PivotMessages.GetClarificationWithTopic(oldTopic!, suggestedTopic!, useChinese);

        return new PivotClarificationRequestEvent
        {
            Timestamp = NowMs(),
            SessionId = sessionId,
            PivotId = pivotId,
            Question = question,
            SuggestedTopic = suggestedTopic,
            Confidence = confidence
        };
    }

    /// <inheritdoc />
    public string GetCompletionMessage(
        string? newTopic,
        IReadOnlyList<string>? preserveAspects,
        bool useChinese = true)
    {
        if (!string.IsNullOrWhiteSpace(newTopic))
        {
            if (preserveAspects?.Count > 0)
            {
                return PivotMessages.GetCompletionWithPreserve(
                    string.Join("、", preserveAspects),
                    useChinese);
            }
            return PivotMessages.GetCompletionSuccess(newTopic, useChinese);
        }

        return useChinese
            ? "研究方向已更新"
            : "Research direction updated";
    }

    /// <inheritdoc />
    public string GetRollbackAvailableMessage(int rollbackWindowMinutes, bool useChinese = true)
    {
        return PivotMessages.GetRollbackAvailable(rollbackWindowMinutes, useChinese);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Interface for emitting pivot feedback events.
/// </summary>
public interface IPivotFeedbackEmitter
{
    /// <summary>
    /// Creates a pivot detected event.
    /// </summary>
    PivotDetectedEvent CreateDetectedEvent(
        string sessionId,
        string pivotId,
        DirectionChangeIntent intent);

    /// <summary>
    /// Creates a pivot started event.
    /// </summary>
    PivotStartedEvent CreateStartedEvent(
        string sessionId,
        string pivotId,
        string? oldDirection,
        string? newDirection);

    /// <summary>
    /// Creates a progress event for a specific stage.
    /// </summary>
    PivotProgressEvent CreateProgressEvent(
        string sessionId,
        string pivotId,
        PivotProgressStage stage,
        int? count = null,
        double? progress = null,
        bool useChinese = true);

    /// <summary>
    /// Creates a pivot completed event.
    /// </summary>
    PivotCompletedEvent CreateCompletedEvent(
        string sessionId,
        string pivotId,
        PivotOperation operation,
        bool useChinese = true);

    /// <summary>
    /// Creates a pivot error event.
    /// </summary>
    PivotErrorEvent CreateErrorEvent(
        string sessionId,
        string pivotId,
        string errorMessage,
        string? errorCode = null);

    /// <summary>
    /// Creates a clarification request event.
    /// </summary>
    PivotClarificationRequestEvent CreateClarificationRequestEvent(
        string sessionId,
        string pivotId,
        string? suggestedTopic,
        double confidence,
        string? oldTopic = null,
        bool useChinese = true);

    /// <summary>
    /// Gets a localized completion message.
    /// </summary>
    string GetCompletionMessage(
        string? newTopic,
        IReadOnlyList<string>? preserveAspects,
        bool useChinese = true);

    /// <summary>
    /// Gets a localized rollback availability message.
    /// </summary>
    string GetRollbackAvailableMessage(int rollbackWindowMinutes, bool useChinese = true);
}

/// <summary>
/// Progress stages during pivot execution.
/// </summary>
public enum PivotProgressStage
{
    /// <summary>Cancelling pending plan nodes.</summary>
    CancellingPlans,

    /// <summary>Preserving completed knowledge nodes.</summary>
    PreservingKnowledge,

    /// <summary>Notifying subagents of direction change.</summary>
    NotifyingAgents,

    /// <summary>Updating the knowledge graph DAG.</summary>
    UpdatingDag
}
