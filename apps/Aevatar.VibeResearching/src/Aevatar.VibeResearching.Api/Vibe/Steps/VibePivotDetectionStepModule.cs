using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.AGUI;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe;
using VibeResearching.Vibe.Pivot;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibePivotDetectionStepModule : VibeStepModuleBase
{
    private readonly VibePivot _pivot;
    private readonly BriefStore _brief;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibePivotDetectionStepModule> _logger;

    public VibePivotDetectionStepModule(
        VibePivot pivot,
        BriefStore brief,
        ResearchSessionManager sessions,
        ILogger<VibePivotDetectionStepModule> logger)
    {
        _pivot = pivot ?? throw new ArgumentNullException(nameof(pivot));
        _brief = brief ?? throw new ArgumentNullException(nameof(brief));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_pivot_detection";
    public override string StepType => "vibe_pivot_detection";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_pivot_detection requires session_id");

        var runId = ResolveRunId(coordinator);
        var question = ResolveQuestion(coordinator);
        var session = _sessions.GetOrCreate(sessionId);

        session.Events.Publish(new StepStartedEvent
        {
            Timestamp = NowMs(),
            StepName = "vibe.pivot_detection"
        });

        DirectionChangeIntent? intent = null;
        try
        {
            var currentDirection = await GetCurrentDirectionAsync(session.Id, ct);
            intent = await DetectDirectionChangeAsync(
                session,
                runId,
                question,
                currentDirection,
                delta => EmitAssistantDelta(session, runId, delta),
                ct);

            if (intent == null)
            {
                _logger.LogDebug("Pivot detection returned null for session {SessionId}", session.Id);
                return PrimitiveResult.Ok(new Dictionary<string, object> { ["ok"] = false });
            }

            var pivotEmitter = _pivot.FeedbackEmitter;
            var pivotId = (intent.PivotId ?? string.Empty).Trim();

            if (intent is { IsDirectionChange: true } &&
                intent.Confidence >= _pivot.Options.ConfidenceThreshold)
            {
                try
                {
                    if (pivotId.Length == 0)
                        pivotId = $"pivot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                    intent = intent with { PivotId = pivotId };

                    session.Events.Publish(pivotEmitter.CreateDetectedEvent(session.Id, pivotId, intent));

                    if (intent.NeedsClarification)
                    {
                        session.Events.Publish(pivotEmitter.CreateClarificationRequestEvent(
                            session.Id,
                            pivotId,
                            intent.NewTopic,
                            intent.Confidence,
                            oldTopic: currentDirection));
                    }

                    session.Events.Publish(pivotEmitter.CreateStartedEvent(
                        session.Id,
                        pivotId,
                        currentDirection,
                        intent.NewTopic));

                    _logger.LogInformation(
                        "Enqueueing pivot for session {SessionId}: {OldDirection} -> {NewDirection}",
                        session.Id, currentDirection ?? "(none)", intent.NewTopic ?? "(new)");

                    var capturedDirection = currentDirection;
                    var queueResult = await _pivot.Queue.EnqueueAsync(
                        intent,
                        async cancellationToken =>
                        {
                            var coordResult = await _pivot.AgentCoordinator.CoordinatePivotAsync(
                                intent,
                                capturedDirection,
                                cancellationToken);
                            return coordResult.DagOperation!;
                        },
                        ct);

                    if (queueResult.QueueFull)
                    {
                        _logger.LogWarning("Pivot queue full for session {SessionId}", session.Id);
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            "pivot_queue_full",
                            errorCode: "pivot_queue_full"));
                        EmitAssistantDelta(session, runId, "\n⏳ 研究方向更新队列已满，请稍后重试...\n\n");
                    }
                    else if (queueResult.ErrorMessage != null)
                    {
                        _logger.LogError(
                            "Pivot failed for session {SessionId}: {Error}",
                            session.Id, queueResult.ErrorMessage);
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            queueResult.ErrorMessage,
                            errorCode: "pivot_failed"));
                        EmitAssistantDelta(session, runId, "\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                    }
                    else if (queueResult.Operation != null)
                    {
                        var pivotOp = queueResult.Operation;
                        _logger.LogInformation(
                            "Pivot completed for session {SessionId}: cancelled={Cancelled}, preserved={Preserved}, queued={Queued}",
                            session.Id, pivotOp.CancelledNodeIds.Count, pivotOp.PreservedNodeIds.Count, queueResult.Queued);

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.CancellingPlans,
                            count: pivotOp.CancelledNodeIds.Count,
                            progress: 0.6));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.PreservingKnowledge,
                            count: pivotOp.PreservedNodeIds.Count,
                            progress: 0.8));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.NotifyingAgents,
                            progress: 0.9));

                        session.Events.Publish(pivotEmitter.CreateProgressEvent(
                            session.Id,
                            pivotOp.PivotId,
                            PivotProgressStage.UpdatingDag,
                            progress: 1.0));

                        session.Events.Publish(pivotEmitter.CreateCompletedEvent(
                            session.Id,
                            pivotOp.PivotId,
                            pivotOp));

                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = NowMs(),
                            Name = "aevatar.vibe.pivot_completed",
                            Value = new
                            {
                                sessionId = session.Id,
                                pivotId = pivotOp.PivotId,
                                status = pivotOp.Status.ToString(),
                                cancelledCount = pivotOp.CancelledNodeIds.Count,
                                preservedCount = pivotOp.PreservedNodeIds.Count,
                                durationMs = pivotOp.DurationMs,
                                wasQueued = queueResult.Queued
                            }
                        });

                        var queuedNote = queueResult.Queued ? " (队列等待后执行)" : "";
                        EmitAssistantDelta(session, runId,
                            $"\n✓ 研究方向已更新完成{queuedNote} (取消了 {pivotOp.CancelledNodeIds.Count} 个待执行计划，保留了 {pivotOp.PreservedNodeIds.Count} 个已完成成果)\n\n");
                    }
                }
                catch (Exception pivotEx)
                {
                    _logger.LogError(pivotEx, "Pivot execution failed for session {SessionId}", session.Id);
                    if (pivotId.Length > 0)
                    {
                        session.Events.Publish(pivotEmitter.CreateErrorEvent(
                            session.Id,
                            pivotId,
                            pivotEx.Message,
                            errorCode: "pivot_exception"));
                    }
                    EmitAssistantDelta(session, runId, "\n⚠ 研究方向更新失败，将继续使用当前方向\n\n");
                }
            }
            else if (intent is { IsDirectionChange: true })
            {
                if (pivotId.Length == 0)
                    pivotId = $"pivot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                intent = intent with { PivotId = pivotId };
                session.Events.Publish(pivotEmitter.CreateDetectedEvent(session.Id, pivotId, intent));

                if (intent.NeedsClarification)
                {
                    session.Events.Publish(pivotEmitter.CreateClarificationRequestEvent(
                        session.Id,
                        pivotId,
                        intent.NewTopic,
                        intent.Confidence,
                        oldTopic: currentDirection));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direction change detection failed for session {SessionId}", session.Id);
        }
        finally
        {
            session.Events.Publish(new StepFinishedEvent
            {
                Timestamp = NowMs(),
                StepName = "vibe.pivot_detection"
            });
        }

        return PrimitiveResult.Ok(new Dictionary<string, object>
        {
            ["ok"] = true,
            ["is_direction_change"] = intent?.IsDirectionChange ?? false,
            ["confidence"] = intent?.Confidence ?? 0.0,
            ["needs_clarification"] = intent?.NeedsClarification ?? false,
            ["new_topic"] = intent?.NewTopic ?? string.Empty
        });
    }

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

            _logger.LogDebug(
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
                _logger.LogDebug(
                    "No direction change detected for session {SessionId} (confidence: {Confidence:F2})",
                    session.Id, intent.Confidence);
                return intent;
            }

            _logger.LogInformation(
                "Direction change detected for session {SessionId}: NewTopic={NewTopic}, Confidence={Confidence:F2}, NeedsClarification={NeedsClarification}",
                session.Id, intent.NewTopic, intent.Confidence, intent.NeedsClarification);

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
                    newTopic = intent.NewTopic ?? string.Empty,
                    needsClarification = intent.NeedsClarification,
                    preserveAspects = intent.PreserveAspects.ToList()
                }
            });

            if (intent.NeedsClarification ||
                (intent.Confidence >= _pivot.Options.ClarificationThreshold &&
                 intent.Confidence < _pivot.Options.ConfidenceThreshold))
            {
                EmitPivotClarificationRequest(session, intent, emitAssistantDelta);
            }
            else if (intent.Confidence >= _pivot.Options.ConfidenceThreshold)
            {
                EmitPivotNotification(session, intent, emitAssistantDelta);
            }

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Direction change detection failed for session {SessionId}",
                session.Id);
            return null;
        }
    }

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

        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.pivot_clarification_requested",
            Value = new
            {
                sessionId = session.Id,
                pivotId = intent.MessageId,
                suggestedTopic = intent.NewTopic ?? string.Empty,
                confidence = intent.Confidence
            }
        });
    }

    private static void EmitPivotNotification(
        ResearchSession session,
        DirectionChangeIntent intent,
        Action<string> emitAssistantDelta)
    {
        emitAssistantDelta("\n\n---\n");
        emitAssistantDelta("### 研究方向变更\n\n");
        emitAssistantDelta($"已检测到研究方向变更请求 (置信度: {intent.Confidence:P0})\n\n");

        if (!string.IsNullOrWhiteSpace(intent.NewTopic))
            emitAssistantDelta($"新方向: **{intent.NewTopic}**\n\n");

        if (intent.PreserveAspects.Count > 0)
        {
            emitAssistantDelta("保留以下研究成果:\n");
            foreach (var aspect in intent.PreserveAspects)
                emitAssistantDelta($"- {aspect}\n");
            emitAssistantDelta("\n");
        }

        emitAssistantDelta("系统正在调整研究计划...\n\n");
        emitAssistantDelta("---\n\n");

        session.Events.Publish(new CustomEvent
        {
            Timestamp = NowMs(),
            Name = "aevatar.vibe.pivot_initiated",
            Value = new
            {
                sessionId = session.Id,
                pivotId = intent.MessageId,
                newTopic = intent.NewTopic ?? string.Empty,
                confidence = intent.Confidence,
                preserveAspects = intent.PreserveAspects.ToList()
            }
        });
    }

    private async Task<string?> GetCurrentDirectionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var brief = await _brief.LoadAsync(sessionId, ct);
            if (brief?.Version > 0 && !string.IsNullOrWhiteSpace(brief.RewrittenQuestion))
                return brief.RewrittenQuestion;
        }
        catch
        {
            // best-effort
        }

        return null;
    }

    private static void EmitAssistantDelta(ResearchSession session, string runId, string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        var messageId = $"msg:{session.Id}:assistant:{runId}";
        session.AppendToMessage(messageId, role: "assistant", delta);

        session.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = NowMs(),
            MessageId = messageId,
            Delta = delta
        });
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
