using Microsoft.Extensions.Logging;
using Aevatar.Agents.AGUI;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Agents.Pivot;
using Aevatar.VibeResearching.Agents.Pivot.Models;

namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Partial class containing pivot-related orchestration logic.
/// </summary>
public sealed partial class VibeOrchestrator
{
    /// <summary>
    /// Detects if the user message indicates a research direction change.
    /// This runs asynchronously and should not block the main round execution.
    /// </summary>
    /// <param name="session">Current research session.</param>
    /// <param name="runId">Current run identifier (used as message ID).</param>
    /// <param name="question">User's message text.</param>
    /// <param name="currentDirection">Current research direction (from brief or DAG context).</param>
    /// <param name="emitAssistantDelta">Callback for streaming output to user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detection result, or null if detection failed.</returns>
    private async Task<DirectionChangeIntent?> DetectDirectionChangeAsync(
        ResearchSession session,
        string runId,
        string question,
        string? currentDirection,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        try
        {
            var messageId = $"msg:{session.Id}:user:{runId}";

            _host.Logger.LogDebug(
                "Starting direction change detection for session {SessionId}, message {MessageId}",
                session.Id, messageId);

            var intent = await _pivot.DirectionDetector.DetectAsync(
                session.Id,
                messageId,
                question,
                currentDirection,
                ct);

            if (!intent.IsDirectionChange)
            {
                _host.Logger.LogDebug(
                    "No direction change detected for session {SessionId} (confidence: {Confidence:F2})",
                    session.Id, intent.Confidence);
                return intent;
            }

            _host.Logger.LogInformation(
                "Direction change detected for session {SessionId}: NewTopic={NewTopic}, Confidence={Confidence:F2}, NeedsClarification={NeedsClarification}",
                session.Id, intent.NewTopic, intent.Confidence, intent.NeedsClarification);

            // Emit pivot detection event for AG-UI
            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.pivot_detected",
                Value = new
                {
                    sessionId = session.Id,
                    messageId,
                    isDirectionChange = intent.IsDirectionChange,
                    confidence = intent.Confidence,
                    newTopic = intent.NewTopic ?? "",
                    needsClarification = intent.NeedsClarification,
                    preserveAspects = intent.PreserveAspects.ToList()
                }
            });

            // Handle based on confidence thresholds
            if (intent.NeedsClarification ||
                (intent.Confidence >= _pivot.Options.ClarificationThreshold &&
                 intent.Confidence < _pivot.Options.ConfidenceThreshold))
            {
                // Request user clarification
                EmitPivotClarificationRequest(session, intent, emitAssistantDelta);
            }
            else if (intent.Confidence >= _pivot.Options.ConfidenceThreshold)
            {
                // High confidence - will trigger pivot in later phases
                EmitPivotNotification(session, intent, emitAssistantDelta);
            }

            return intent;
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex,
                "Direction change detection failed for session {SessionId}",
                session.Id);
            return null;
        }
    }

    /// <summary>
    /// Emits a clarification request to the user via the assistant stream.
    /// </summary>
    private static void EmitPivotClarificationRequest(
        ResearchSession session,
        DirectionChangeIntent intent,
        Action<string> emitAssistantDelta)
    {
        var topicHint = string.IsNullOrWhiteSpace(intent.NewTopic)
            ? "一个新的研究方向"
            : intent.NewTopic;

        emitAssistantDelta("\n\n---\n");
        emitAssistantDelta("### 检测到可能的研究方向变更\n\n");
        emitAssistantDelta($"您似乎想将研究方向转向: **{topicHint}**\n\n");
        emitAssistantDelta("请确认：\n");
        emitAssistantDelta("- 输入 \"是\" 或 \"确认\" 来执行方向变更\n");
        emitAssistantDelta("- 输入 \"否\" 或 \"取消\" 来继续当前研究\n");
        emitAssistantDelta("- 或者更明确地描述您想要的新研究方向\n\n");
        emitAssistantDelta("---\n\n");

        // Also emit a structured event for programmatic handling
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.pivot_clarification_requested",
            Value = new
            {
                sessionId = session.Id,
                pivotId = intent.MessageId,
                suggestedTopic = intent.NewTopic ?? "",
                confidence = intent.Confidence
            }
        });
    }

    /// <summary>
    /// Emits a notification that a high-confidence pivot was detected.
    /// </summary>
    private static void EmitPivotNotification(
        ResearchSession session,
        DirectionChangeIntent intent,
        Action<string> emitAssistantDelta)
    {
        emitAssistantDelta("\n\n---\n");
        emitAssistantDelta("### 研究方向变更\n\n");
        emitAssistantDelta($"已检测到研究方向变更请求 (置信度: {intent.Confidence:P0})\n\n");

        if (!string.IsNullOrWhiteSpace(intent.NewTopic))
        {
            emitAssistantDelta($"新方向: **{intent.NewTopic}**\n\n");
        }

        if (intent.PreserveAspects.Count > 0)
        {
            emitAssistantDelta("保留以下研究成果:\n");
            foreach (var aspect in intent.PreserveAspects)
            {
                emitAssistantDelta($"- {aspect}\n");
            }
            emitAssistantDelta("\n");
        }

        emitAssistantDelta("系统正在调整研究计划...\n\n");
        emitAssistantDelta("---\n\n");

        // Emit event for pivot initiation
        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.pivot_initiated",
            Value = new
            {
                sessionId = session.Id,
                pivotId = intent.MessageId,
                newTopic = intent.NewTopic ?? "",
                confidence = intent.Confidence,
                preserveAspects = intent.PreserveAspects.ToList()
            }
        });
    }

    /// <summary>
    /// Extracts current research direction from the brief or DAG context.
    /// </summary>
    private async Task<string?> GetCurrentDirectionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var brief = await _core.Brief.LoadAsync(sessionId, ct);
            if (brief?.Version > 0 && !string.IsNullOrWhiteSpace(brief.RewrittenQuestion))
            {
                return brief.RewrittenQuestion;
            }
        }
        catch
        {
            // Best effort - continue without direction context
        }

        return null;
    }
}
