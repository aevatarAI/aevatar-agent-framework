using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Vibe.Pivot;
using ScientificResearchAssistant.Vibe.Pivot.Models;
using Shouldly;

namespace ScientificResearchAssistant.Api.Tests.Vibe.Pivot;

public sealed class PivotQueueTests
{
    private readonly IOptions<PivotOptions> _options;

    public PivotQueueTests()
    {
        _options = Options.Create(new PivotOptions
        {
            MaxQueueDepth = 3
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Basic Queue Operations
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ExecutesPivotImmediately_WhenQueueEmpty()
    {
        // Arrange
        var queue = CreateQueue();
        var intent = CreateIntent("session1");
        var executed = false;

        // Act
        var result = await queue.EnqueueAsync(
            intent,
            async _ =>
            {
                executed = true;
                return CreateOperation("session1", "pivot1");
            });

        // Assert
        result.SessionId.ShouldBe("session1");
        result.Queued.ShouldBeFalse();
        result.Position.ShouldBe(0);
        result.Operation.ShouldNotBeNull();
        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_SerializesRequests_ForSameSession()
    {
        // Arrange
        var queue = CreateQueue();
        var intent1 = CreateIntent("session1");
        var intent2 = CreateIntent("session1");
        var executionOrder = new List<int>();
        var barrier = new TaskCompletionSource();

        // Start first pivot (will wait on barrier)
        var task1 = queue.EnqueueAsync(
            intent1,
            async _ =>
            {
                executionOrder.Add(1);
                await barrier.Task;
                return CreateOperation("session1", "pivot1");
            });

        // Give time for first task to start
        await Task.Delay(50);

        // Start second pivot (should queue)
        var task2 = Task.Run(async () =>
        {
            return await queue.EnqueueAsync(
                intent2,
                async _ =>
                {
                    executionOrder.Add(2);
                    return CreateOperation("session1", "pivot2");
                });
        });

        // Wait a moment for task2 to start waiting
        await Task.Delay(100);

        // At this point, task1 should be executing, task2 should be waiting
        queue.IsProcessing("session1").ShouldBeTrue();

        // Release the barrier
        barrier.SetResult();

        // Wait for both to complete
        await Task.WhenAll(task1, task2);

        // Assert execution order
        executionOrder.Count.ShouldBe(2);
        executionOrder[0].ShouldBe(1);
        executionOrder[1].ShouldBe(2);
    }

    [Fact]
    public async Task EnqueueAsync_AllowsParallelExecution_ForDifferentSessions()
    {
        // Arrange
        var queue = CreateQueue();
        var intent1 = CreateIntent("session1");
        var intent2 = CreateIntent("session2");
        var concurrent = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        async Task<PivotOperation> Executor(string session, CancellationToken ct)
        {
            lock (lockObj)
            {
                concurrent++;
                maxConcurrent = Math.Max(maxConcurrent, concurrent);
            }

            await Task.Delay(100);

            lock (lockObj)
            {
                concurrent--;
            }

            return CreateOperation(session, $"pivot_{session}");
        }

        // Act
        var tasks = new[]
        {
            queue.EnqueueAsync(intent1, ct => Executor("session1", ct)),
            queue.EnqueueAsync(intent2, ct => Executor("session2", ct))
        };

        await Task.WhenAll(tasks);

        // Assert - both should have executed concurrently
        maxConcurrent.ShouldBe(2);
    }

    [Fact]
    public async Task EnqueueAsync_RejectsRequest_WhenQueueFull()
    {
        // Arrange - use maxDepth of 1 for easier testing
        var options = Options.Create(new PivotOptions { MaxQueueDepth = 1 });
        var queue = new PivotQueue(options, NullLogger<PivotQueue>.Instance);

        var intent1 = CreateIntent("session1");
        var intent2 = CreateIntent("session1");
        var barrier = new TaskCompletionSource();

        // Fill the queue
        var task1 = queue.EnqueueAsync(
            intent1,
            async _ =>
            {
                await barrier.Task;
                return CreateOperation("session1", "pivot1");
            });

        // Give time for first task to start
        await Task.Delay(50);

        // Try to add another (should be rejected)
        var result = await queue.EnqueueAsync(
            intent2,
            _ => Task.FromResult(CreateOperation("session1", "pivot2")));

        // Assert
        result.QueueFull.ShouldBeTrue();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("full");

        // Cleanup
        barrier.SetResult();
        await task1;
    }

    // ─────────────────────────────────────────────────────────────
    //  Queue Status
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetQueueDepth_ReturnsZero_WhenNoSession()
    {
        // Arrange
        var queue = CreateQueue();

        // Act
        var depth = queue.GetQueueDepth("unknown_session");

        // Assert
        depth.ShouldBe(0);
    }

    [Fact]
    public async Task IsProcessing_ReturnsTrue_DuringExecution()
    {
        // Arrange
        var queue = CreateQueue();
        var intent = CreateIntent("session1");
        var barrier = new TaskCompletionSource();
        var isProcessingDuringExecution = false;

        // Act
        var task = queue.EnqueueAsync(
            intent,
            async _ =>
            {
                isProcessingDuringExecution = queue.IsProcessing("session1");
                await barrier.Task;
                return CreateOperation("session1", "pivot1");
            });

        await Task.Delay(50);

        // Assert - should be processing
        queue.IsProcessing("session1").ShouldBeTrue();

        barrier.SetResult();
        await task;

        // After completion, should not be processing
        queue.IsProcessing("session1").ShouldBeFalse();
        isProcessingDuringExecution.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllQueueStatuses_ReturnsAllSessionStatuses()
    {
        // Arrange
        var queue = CreateQueue();
        var barriers = new Dictionary<string, TaskCompletionSource>
        {
            ["session1"] = new(),
            ["session2"] = new()
        };

        // Start pivots for two sessions
        var tasks = new[]
        {
            queue.EnqueueAsync(CreateIntent("session1"), async _ =>
            {
                await barriers["session1"].Task;
                return CreateOperation("session1", "pivot1");
            }),
            queue.EnqueueAsync(CreateIntent("session2"), async _ =>
            {
                await barriers["session2"].Task;
                return CreateOperation("session2", "pivot2");
            })
        };

        await Task.Delay(50);

        // Act
        var statuses = queue.GetAllQueueStatuses();

        // Assert
        statuses.Count.ShouldBe(2);
        statuses.ShouldContain(s => s.SessionId == "session1" && s.IsProcessing);
        statuses.ShouldContain(s => s.SessionId == "session2" && s.IsProcessing);

        // Cleanup
        foreach (var barrier in barriers.Values)
        {
            barrier.SetResult();
        }
        await Task.WhenAll(tasks);
    }

    // ─────────────────────────────────────────────────────────────
    //  Error Handling
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ReturnsError_WhenExecutorThrows()
    {
        // Arrange
        var queue = CreateQueue();
        var intent = CreateIntent("session1");

        // Act
        var result = await queue.EnqueueAsync(
            intent,
            _ => throw new InvalidOperationException("Test error"));

        // Assert
        result.Operation.ShouldBeNull();
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Test error");
    }

    [Fact]
    public async Task EnqueueAsync_ReleasesLock_AfterError()
    {
        // Arrange
        var queue = CreateQueue();
        var intent1 = CreateIntent("session1");
        var intent2 = CreateIntent("session1");

        // First call throws
        await queue.EnqueueAsync(
            intent1,
            _ => throw new InvalidOperationException("Test error"));

        // Second call should still work
        var result = await queue.EnqueueAsync(
            intent2,
            _ => Task.FromResult(CreateOperation("session1", "pivot2")));

        // Assert
        result.Operation.ShouldNotBeNull();
    }

    // ─────────────────────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanupSession_RemovesSessionQueue()
    {
        // Arrange
        var queue = CreateQueue();
        var intent = CreateIntent("session1");

        await queue.EnqueueAsync(
            intent,
            _ => Task.FromResult(CreateOperation("session1", "pivot1")));

        // Verify session exists
        queue.GetAllQueueStatuses().ShouldContain(s => s.SessionId == "session1");

        // Act
        queue.CleanupSession("session1");

        // Assert
        queue.GetQueueDepth("session1").ShouldBe(0);
        queue.IsProcessing("session1").ShouldBeFalse();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper Methods
    // ─────────────────────────────────────────────────────────────

    private PivotQueue CreateQueue()
    {
        return new PivotQueue(_options, NullLogger<PivotQueue>.Instance);
    }

    private static DirectionChangeIntent CreateIntent(string sessionId)
    {
        return new DirectionChangeIntent
        {
            SessionId = sessionId,
            MessageId = "msg1",
            IsDirectionChange = true,
            Confidence = 0.85,
            NewTopic = "new topic"
        };
    }

    private static PivotOperation CreateOperation(string sessionId, string pivotId)
    {
        var intent = CreateIntent(sessionId);
        var op = PivotOperation.Create(intent);
        op.Complete(PivotStatus.Completed);
        return op;
    }
}
