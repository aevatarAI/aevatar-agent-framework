using Aevatar.Agents.AGUI;
using Microsoft.Extensions.Logging.Abstractions;
using ScientificResearchAssistant.Vibe.Pivot;
using ScientificResearchAssistant.Vibe.Pivot.Messages;
using ScientificResearchAssistant.Vibe.Pivot.Models;
using Shouldly;

namespace ScientificResearchAssistant.Api.Tests.Vibe.Pivot;

public sealed class UserFeedbackTests
{
    private readonly PivotFeedbackEmitter _emitter;

    public UserFeedbackTests()
    {
        _emitter = new PivotFeedbackEmitter(NullLogger<PivotFeedbackEmitter>.Instance);
    }

    // ─────────────────────────────────────────────────────────────
    //  AG-UI Event Creation Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDetectedEvent_ReturnsCorrectEventType()
    {
        // Arrange
        var intent = CreateIntent();

        // Act
        var evt = _emitter.CreateDetectedEvent("session1", "pivot1", intent);

        // Assert
        evt.Type.ShouldBe("PIVOT_DETECTED");
        evt.SessionId.ShouldBe("session1");
        evt.PivotId.ShouldBe("pivot1");
        evt.Confidence.ShouldBe(0.85);
        evt.NewTopic.ShouldBe("量子计算");
        evt.NeedsClarification.ShouldBeFalse();
        evt.Timestamp.ShouldNotBeNull();
        evt.Timestamp!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CreateStartedEvent_ReturnsCorrectEventType()
    {
        // Act
        var evt = _emitter.CreateStartedEvent("session1", "pivot1", "旧方向", "新方向");

        // Assert
        evt.Type.ShouldBe("PIVOT_STARTED");
        evt.SessionId.ShouldBe("session1");
        evt.PivotId.ShouldBe("pivot1");
        evt.OldDirection.ShouldBe("旧方向");
        evt.NewDirection.ShouldBe("新方向");
    }

    [Fact]
    public void CreateProgressEvent_CancellingPlans_ReturnsChineseMessage()
    {
        // Act
        var evt = _emitter.CreateProgressEvent(
            "session1", "pivot1",
            PivotProgressStage.CancellingPlans,
            count: 5,
            useChinese: true);

        // Assert
        evt.Type.ShouldBe("PIVOT_PROGRESS");
        evt.Stage.ShouldBe("CancellingPlans");
        evt.Message.ShouldContain("取消");
        evt.Message.ShouldContain("5");
    }

    [Fact]
    public void CreateProgressEvent_CancellingPlans_ReturnsEnglishMessage()
    {
        // Act
        var evt = _emitter.CreateProgressEvent(
            "session1", "pivot1",
            PivotProgressStage.CancellingPlans,
            count: 3,
            useChinese: false);

        // Assert
        evt.Message.ShouldContain("Cancelling");
        evt.Message.ShouldContain("3");
    }

    [Fact]
    public void CreateProgressEvent_PreservingKnowledge_ReturnsCorrectMessage()
    {
        // Act
        var evt = _emitter.CreateProgressEvent(
            "session1", "pivot1",
            PivotProgressStage.PreservingKnowledge,
            count: 10,
            useChinese: true);

        // Assert
        evt.Stage.ShouldBe("PreservingKnowledge");
        evt.Message.ShouldContain("保留");
        evt.Message.ShouldContain("10");
    }

    [Fact]
    public void CreateProgressEvent_NotifyingAgents_ReturnsCorrectMessage()
    {
        // Act
        var evt = _emitter.CreateProgressEvent(
            "session1", "pivot1",
            PivotProgressStage.NotifyingAgents,
            useChinese: true);

        // Assert
        evt.Stage.ShouldBe("NotifyingAgents");
        evt.Message.ShouldContain("通知");
    }

    [Fact]
    public void CreateProgressEvent_UpdatingDag_ReturnsCorrectMessage()
    {
        // Act
        var evt = _emitter.CreateProgressEvent(
            "session1", "pivot1",
            PivotProgressStage.UpdatingDag,
            useChinese: true);

        // Assert
        evt.Stage.ShouldBe("UpdatingDag");
        evt.Message.ShouldContain("更新");
    }

    [Fact]
    public void CreateCompletedEvent_ReturnsCorrectCounts()
    {
        // Arrange
        var operation = CreatePivotOperation(
            cancelledCount: 3,
            preservedCount: 5,
            newCount: 2);

        // Act
        var evt = _emitter.CreateCompletedEvent("session1", "pivot1", operation);

        // Assert
        evt.Type.ShouldBe("PIVOT_COMPLETED");
        evt.CancelledCount.ShouldBe(3);
        evt.PreservedCount.ShouldBe(5);
        evt.NewCount.ShouldBe(2);
        evt.Summary.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void CreateErrorEvent_ReturnsCorrectErrorInfo()
    {
        // Act
        var evt = _emitter.CreateErrorEvent("session1", "pivot1", "Connection failed", "CONN_ERR");

        // Assert
        evt.Type.ShouldBe("PIVOT_ERROR");
        evt.ErrorMessage.ShouldBe("Connection failed");
        evt.ErrorCode.ShouldBe("CONN_ERR");
    }

    [Fact]
    public void CreateClarificationRequestEvent_WithTopics_ReturnsFormattedQuestion()
    {
        // Act
        var evt = _emitter.CreateClarificationRequestEvent(
            "session1", "pivot1",
            suggestedTopic: "量子计算",
            confidence: 0.55,
            oldTopic: "传统密码学",
            useChinese: true);

        // Assert
        evt.Type.ShouldBe("PIVOT_CLARIFICATION_REQUEST");
        evt.Question.ShouldContain("传统密码学");
        evt.Question.ShouldContain("量子计算");
        evt.Confidence.ShouldBe(0.55);
    }

    [Fact]
    public void CreateClarificationRequestEvent_WithoutTopics_ReturnsSimpleQuestion()
    {
        // Act
        var evt = _emitter.CreateClarificationRequestEvent(
            "session1", "pivot1",
            suggestedTopic: null,
            confidence: 0.5,
            oldTopic: null,
            useChinese: true);

        // Assert
        evt.Question.ShouldContain("更换研究方向");
    }

    // ─────────────────────────────────────────────────────────────
    //  Localized Message Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetCompletionMessage_WithNewTopic_ReturnsFormattedMessage()
    {
        // Act
        var message = _emitter.GetCompletionMessage("量子计算", null, useChinese: true);

        // Assert
        message.ShouldContain("量子计算");
    }

    [Fact]
    public void GetCompletionMessage_WithPreserveAspects_ReturnsPreservationMessage()
    {
        // Act
        var message = _emitter.GetCompletionMessage(
            "量子计算",
            new List<string> { "CNN", "架构" },
            useChinese: true);

        // Assert
        message.ShouldContain("CNN");
        message.ShouldContain("保留");
    }

    [Fact]
    public void GetRollbackAvailableMessage_ReturnsTimeWindow()
    {
        // Act
        var message = _emitter.GetRollbackAvailableMessage(30, useChinese: true);

        // Assert
        message.ShouldContain("30");
        message.ShouldContain("撤销");
    }

    [Fact]
    public void GetRollbackAvailableMessage_English_ReturnsTimeWindow()
    {
        // Act
        var message = _emitter.GetRollbackAvailableMessage(15, useChinese: false);

        // Assert
        message.ShouldContain("15");
        message.ShouldContain("undo");
    }

    // ─────────────────────────────────────────────────────────────
    //  PivotMessages Static Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void PivotMessages_DetectionAcknowledgment_BothLanguages()
    {
        // Assert
        PivotMessages.GetDetectionAcknowledgment(true).ShouldContain("检测到");
        PivotMessages.GetDetectionAcknowledgment(false).ShouldContain("Detected");
    }

    [Fact]
    public void PivotMessages_ClarificationPrompt_BothLanguages()
    {
        // Assert
        PivotMessages.GetClarificationPrompt(true).ShouldContain("更换研究方向");
        PivotMessages.GetClarificationPrompt(false).ShouldContain("research direction");
    }

    [Fact]
    public void PivotMessages_CompletionSuccess_BothLanguages()
    {
        // Assert
        PivotMessages.GetCompletionSuccess("topic", true).ShouldContain("已更新");
        PivotMessages.GetCompletionSuccess("topic", false).ShouldContain("updated");
    }

    [Fact]
    public void PivotMessages_ErrorPivotFailed_FormatsCorrectly()
    {
        // Act
        var zh = PivotMessages.GetErrorPivotFailed("连接超时", true);
        var en = PivotMessages.GetErrorPivotFailed("Connection timeout", false);

        // Assert
        zh.ShouldContain("连接超时");
        en.ShouldContain("Connection timeout");
    }

    // ─────────────────────────────────────────────────────────────
    //  Event Timestamp Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AllEvents_HaveValidTimestamps()
    {
        // Arrange
        var beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        var detected = _emitter.CreateDetectedEvent("s", "p", CreateIntent());
        var started = _emitter.CreateStartedEvent("s", "p", null, null);
        var progress = _emitter.CreateProgressEvent("s", "p", PivotProgressStage.UpdatingDag);
        var completed = _emitter.CreateCompletedEvent("s", "p", CreatePivotOperation(0, 0, 0));
        var error = _emitter.CreateErrorEvent("s", "p", "err");
        var clarify = _emitter.CreateClarificationRequestEvent("s", "p", null, 0.5);

        var afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert - all timestamps should be within test window
        detected.Timestamp.ShouldNotBeNull();
        detected.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
        detected.Timestamp.Value.ShouldBeLessThanOrEqualTo(afterMs);

        started.Timestamp.ShouldNotBeNull();
        progress.Timestamp.ShouldNotBeNull();
        completed.Timestamp.ShouldNotBeNull();
        error.Timestamp.ShouldNotBeNull();
        clarify.Timestamp.ShouldNotBeNull();

        started.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
        progress.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
        completed.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
        error.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
        clarify.Timestamp!.Value.ShouldBeGreaterThanOrEqualTo(beforeMs);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper Methods
    // ─────────────────────────────────────────────────────────────

    private static DirectionChangeIntent CreateIntent()
    {
        return new DirectionChangeIntent
        {
            SessionId = "session1",
            MessageId = "msg1",
            IsDirectionChange = true,
            Confidence = 0.85,
            NewTopic = "量子计算",
            PreserveAspects = new List<string> { "CNN" },
            NeedsClarification = false
        };
    }

    private static PivotOperation CreatePivotOperation(int cancelledCount, int preservedCount, int newCount)
    {
        var op = PivotOperation.Create(CreateIntent());

        for (var i = 0; i < cancelledCount; i++)
            op.CancelledNodeIds.Add($"cancelled_{i}");

        for (var i = 0; i < preservedCount; i++)
            op.PreservedNodeIds.Add($"preserved_{i}");

        for (var i = 0; i < newCount; i++)
            op.NewNodeIds.Add($"new_{i}");

        op.Complete(PivotStatus.Completed);
        return op;
    }
}
