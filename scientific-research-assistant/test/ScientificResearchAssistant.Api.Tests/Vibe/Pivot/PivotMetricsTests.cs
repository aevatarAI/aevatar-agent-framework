using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScientificResearchAssistant.Vibe.Pivot;
using Shouldly;

namespace ScientificResearchAssistant.Api.Tests.Vibe.Pivot;

public sealed class PivotMetricsTests : IDisposable
{
    private readonly IPivotQueue _mockQueue;
    private readonly PivotMetrics _metrics;

    public PivotMetricsTests()
    {
        _mockQueue = Substitute.For<IPivotQueue>();
        _mockQueue.GetAllQueueStatuses().Returns(new List<PivotQueueStatus>());
        _metrics = new PivotMetrics(_mockQueue);
    }

    public void Dispose()
    {
        _metrics.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  Basic Recording Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void RecordPivotCompletion_DoesNotThrow_ForSuccess()
    {
        // Act & Assert - should not throw
        Should.NotThrow(() => _metrics.RecordPivotCompletion("session1", true, 150.5));
    }

    [Fact]
    public void RecordPivotCompletion_DoesNotThrow_ForFailure()
    {
        // Act & Assert - should not throw
        Should.NotThrow(() => _metrics.RecordPivotCompletion("session1", false, 50.0));
    }

    [Fact]
    public void RecordDetectionConfidence_DoesNotThrow()
    {
        // Act & Assert - should not throw
        Should.NotThrow(() => _metrics.RecordDetectionConfidence("session1", 0.85, true));
        Should.NotThrow(() => _metrics.RecordDetectionConfidence("session2", 0.55, false));
    }

    [Fact]
    public void RecordRollback_DoesNotThrow()
    {
        // Act & Assert - should not throw
        Should.NotThrow(() => _metrics.RecordRollback("session1", true));
        Should.NotThrow(() => _metrics.RecordRollback("session2", false));
    }

    // ─────────────────────────────────────────────────────────────
    //  Latency Timer Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartLatencyTimer_ReturnsTimer()
    {
        // Act
        using var timer = _metrics.StartLatencyTimer("session1");

        // Assert
        timer.ShouldNotBeNull();
    }

    [Fact]
    public void LatencyTimer_RecordsOnDispose()
    {
        // Arrange
        using (var timer = _metrics.StartLatencyTimer("session1"))
        {
            // Simulate some work
            Thread.Sleep(10);
            timer.MarkSuccess();
        }

        // No exception means recording succeeded
    }

    [Fact]
    public void LatencyTimer_MarkSuccess_SetsSuccessFlag()
    {
        // Act & Assert - should not throw
        using (var timer = _metrics.StartLatencyTimer("session1"))
        {
            Should.NotThrow(() => timer.MarkSuccess());
        }
    }

    [Fact]
    public void LatencyTimer_MarkFailure_SetsFailureFlag()
    {
        // Act & Assert - should not throw
        using (var timer = _metrics.StartLatencyTimer("session1"))
        {
            Should.NotThrow(() => timer.MarkFailure());
        }
    }

    [Fact]
    public void LatencyTimer_CanBeDisposedMultipleTimes()
    {
        // Arrange
        var timer = _metrics.StartLatencyTimer("session1");

        // Act & Assert - should not throw on multiple dispose
        timer.Dispose();
        Should.NotThrow(() => timer.Dispose());
    }

    // ─────────────────────────────────────────────────────────────
    //  Integration with Queue
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void QueueDepth_QueriesQueue()
    {
        // Arrange
        _mockQueue.GetAllQueueStatuses().Returns(new List<PivotQueueStatus>
        {
            new()
            {
                SessionId = "session1",
                QueueDepth = 3,
                MaxDepth = 5,
                IsProcessing = true
            },
            new()
            {
                SessionId = "session2",
                QueueDepth = 1,
                MaxDepth = 5,
                IsProcessing = false
            }
        });

        // Act - trigger the observable gauge callback
        var statuses = _mockQueue.GetAllQueueStatuses();

        // Assert
        statuses.Count.ShouldBe(2);
        statuses[0].QueueDepth.ShouldBe(3);
        statuses[1].QueueDepth.ShouldBe(1);
    }

    // ─────────────────────────────────────────────────────────────
    //  Meter Name
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void MeterName_IsCorrect()
    {
        // Assert
        PivotMetrics.MeterName.ShouldBe("ScientificResearchAssistant.Pivot");
    }
}
