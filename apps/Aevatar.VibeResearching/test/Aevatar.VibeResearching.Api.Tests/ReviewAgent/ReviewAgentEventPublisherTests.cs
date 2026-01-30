using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Vibe.ReviewAgent;
using Aevatar.VibeResearching.Api.ReviewAgent.Events;

namespace VibeResearching.Api.Tests.ReviewAgent;

/// <summary>
/// Unit tests for ReviewAgentEventPublisher.
/// Tests T037: SSE event publishing.
/// </summary>
public sealed class ReviewAgentEventPublisherTests
{
    private readonly ReviewAgentEventPublisher _publisher;

    public ReviewAgentEventPublisherTests()
    {
        _publisher = new ReviewAgentEventPublisher(
            NullLogger<ReviewAgentEventPublisher>.Instance);
    }

    // ─────────────────────────────────────────────────────────────
    //  Subscription
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_ReceivesPublishedEvents()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingReviewRound);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("status_change");
    }

    [Fact]
    public async Task Unsubscribe_StopsReceivingEvents()
    {
        // Arrange
        var eventCount = 0;
        var subscription = _publisher.Subscribe(_ =>
        {
            eventCount++;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);
        subscription.Dispose();
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingReviewRound);

        // Assert
        eventCount.ShouldBe(1);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveEvents()
    {
        // Arrange
        var count1 = 0;
        var count2 = 0;

        using var sub1 = _publisher.Subscribe(_ =>
        {
            count1++;
            return Task.CompletedTask;
        });
        using var sub2 = _publisher.Subscribe(_ =>
        {
            count2++;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);

        // Assert
        count1.ShouldBe(1);
        count2.ShouldBe(1);
    }

    // ─────────────────────────────────────────────────────────────
    //  Status Change Events
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishStatusChangeAsync_EmitsCorrectEventType()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.WorkingCleanupRound);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("status_change");
        receivedEvent.ShouldBeOfType<StatusChangeEvent>();
        var statusEvent = (StatusChangeEvent)receivedEvent;
        statusEvent.Status.ShouldBe("WorkingCleanupRound");
    }

    [Fact]
    public async Task PublishStatusChangeAsync_IncludesNextScheduledAt()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });
        var nextScheduled = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle, nextScheduled);

        // Assert
        receivedEvent.ShouldNotBeNull();
        var statusEvent = (StatusChangeEvent)receivedEvent;
        statusEvent.NextScheduledAt.ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────
    //  Node Progress Events
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishNodeProgressAsync_EmitsCorrectEventType()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishNodeProgressAsync(
            "node-123",
            "Test Node",
            "Test explanation",
            5, 10, 2,
            ReviewResult.Passed);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("node_review_progress");
        receivedEvent.ShouldBeOfType<NodeReviewProgressEvent>();
        var progressEvent = (NodeReviewProgressEvent)receivedEvent;
        progressEvent.NodeId.ShouldBe("node-123");
        progressEvent.NodeLabel.ShouldBe("Test Node");
        progressEvent.NodesReviewed.ShouldBe(5);
        progressEvent.NodesPending.ShouldBe(10);
        progressEvent.NodesDeactivated.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────────
    //  Token Stream Events
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishTokenAsync_EmitsCorrectEventType()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishTokenAsync(
            "agent-1",
            "coordinator",
            "Hello",
            isComplete: false);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("token_stream");
        receivedEvent.ShouldBeOfType<TokenStreamEvent>();
        var tokenEvent = (TokenStreamEvent)receivedEvent;
        tokenEvent.AgentId.ShouldBe("agent-1");
        tokenEvent.AgentRole.ShouldBe("coordinator");
        tokenEvent.Token.ShouldBe("Hello");
        tokenEvent.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishTokenAsync_SetsIsComplete_WhenTrue()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _publisher.PublishTokenAsync(
            "agent-1",
            "worker",
            " World",
            isComplete: true);

        // Assert
        var tokenEvent = (TokenStreamEvent)receivedEvent!;
        tokenEvent.IsComplete.ShouldBeTrue();
    }

    // ─────────────────────────────────────────────────────────────
    //  Iteration Complete Events
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishIterationCompleteAsync_EmitsCorrectEventType()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });
        var summary = new ReviewIterationSummary
        {
            IterationId = "iter-001",
            NodesReviewed = 10,
            NodesValid = 8,
            NodesDeactivated = 2,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _publisher.PublishIterationCompleteAsync("iter-001", summary);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("iteration_complete");
        receivedEvent.ShouldBeOfType<IterationCompleteEvent>();
        var iterEvent = (IterationCompleteEvent)receivedEvent;
        iterEvent.IterationId.ShouldBe("iter-001");
        iterEvent.Summary.ShouldNotBeNull();
        iterEvent.Summary.NodesReviewed.ShouldBe(10);
    }

    // ─────────────────────────────────────────────────────────────
    //  Cleanup Progress Events
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishCleanupProgressAsync_EmitsCorrectEventType()
    {
        // Arrange
        ReviewAgentEvent? receivedEvent = null;
        using var subscription = _publisher.Subscribe(evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });
        var removedNodes = new List<RemovedNodeInfo>
        {
            new() { NodeId = "node-1", NodeLabel = "Node 1", DeactivatedReason = "Test" },
            new() { NodeId = "node-2", NodeLabel = "Node 2", DeactivatedReason = "Test" }
        };

        // Act
        await _publisher.PublishCleanupProgressAsync(2, removedNodes);

        // Assert
        receivedEvent.ShouldNotBeNull();
        receivedEvent.Type.ShouldBe("cleanup_progress");
        receivedEvent.ShouldBeOfType<CleanupProgressEvent>();
        var cleanupEvent = (CleanupProgressEvent)receivedEvent;
        cleanupEvent.NodesRemoved.ShouldBe(2);
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Handling
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_ContinuesForOtherSubscribers_WhenOneThrows()
    {
        // Arrange
        var count = 0;

        using var sub1 = _publisher.Subscribe(_ => throw new InvalidOperationException("Test error"));
        using var sub2 = _publisher.Subscribe(_ =>
        {
            count++;
            return Task.CompletedTask;
        });

        // Act - should not throw
        await _publisher.PublishStatusChangeAsync(ReviewAgentStatus.Idle);

        // Assert - second subscriber should still receive the event
        count.ShouldBe(1);
    }
}
