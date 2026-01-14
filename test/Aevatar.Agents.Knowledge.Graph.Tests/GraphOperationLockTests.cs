using Aevatar.Agents.Knowledge.Graph.Services;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Knowledge.Graph.Tests;

/// <summary>
/// Unit tests for GraphOperationLock concurrency per FR-015a.
/// </summary>
public class GraphOperationLockTests : IDisposable
{
    private readonly GraphOperationLock _lock = new();

    public void Dispose()
    {
        _lock.Dispose();
    }

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_SingleAcquire_Succeeds()
    {
        using var handle = await _lock.AcquireAsync("session-1");

        handle.ShouldNotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_DifferentSessions_AllowsConcurrent()
    {
        var acquired = new List<bool>();
        var tasks = new[]
        {
            AcquireAndRecordAsync("session-1", acquired),
            AcquireAndRecordAsync("session-2", acquired),
            AcquireAndRecordAsync("session-3", acquired)
        };

        await Task.WhenAll(tasks);

        acquired.Count.ShouldBe(3);
        acquired.ShouldAllBe(a => a);
    }

    private async Task AcquireAndRecordAsync(string sessionId, List<bool> acquired)
    {
        using var handle = await _lock.AcquireAsync(sessionId);
        lock (acquired)
        {
            acquired.Add(handle != null);
        }
        await Task.Delay(10); // Hold lock briefly
    }

    [Fact]
    public async Task AcquireAsync_SameSession_Serializes()
    {
        var executionOrder = new List<int>();
        var startGate = new TaskCompletionSource();

        var task1 = Task.Run(async () =>
        {
            await startGate.Task;
            using var handle = await _lock.AcquireAsync("session-1");
            lock (executionOrder) { executionOrder.Add(1); }
            await Task.Delay(50); // Hold lock
            lock (executionOrder) { executionOrder.Add(2); }
        });

        var task2 = Task.Run(async () =>
        {
            await startGate.Task;
            await Task.Delay(10); // Ensure task1 acquires first
            using var handle = await _lock.AcquireAsync("session-1");
            lock (executionOrder) { executionOrder.Add(3); }
        });

        startGate.SetResult();
        await Task.WhenAll(task1, task2);

        // Task2 should only start after task1 releases (executes 1, 2, then 3)
        executionOrder.IndexOf(2).ShouldBeLessThan(executionOrder.IndexOf(3));
    }

    [Fact]
    public async Task AcquireAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var localLock = new GraphOperationLock();
        localLock.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(
            () => localLock.AcquireAsync("session-1"));
    }

    [Fact]
    public async Task AcquireAsync_ReleaseAndReacquire_Works()
    {
        using (var handle1 = await _lock.AcquireAsync("session-1"))
        {
            handle1.ShouldNotBeNull();
        }

        using (var handle2 = await _lock.AcquireAsync("session-1"))
        {
            handle2.ShouldNotBeNull();
        }
    }

    #endregion

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_Available_ReturnsHandle()
    {
        var handle = await _lock.TryAcquireAsync("session-1", TimeSpan.FromSeconds(1));

        handle.ShouldNotBeNull();
        handle.Dispose();
    }

    [Fact]
    public async Task TryAcquireAsync_Held_TimesOut()
    {
        // Hold the lock
        var holdTask = Task.Run(async () =>
        {
            using var hold = await _lock.AcquireAsync("session-1");
            await Task.Delay(500);
        });

        // Give time for holdTask to acquire
        await Task.Delay(50);

        // Try to acquire with short timeout
        var handle = await _lock.TryAcquireAsync("session-1", TimeSpan.FromMilliseconds(50));

        handle.ShouldBeNull();

        await holdTask;
    }

    [Fact]
    public async Task TryAcquireAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var localLock = new GraphOperationLock();
        localLock.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(
            () => localLock.TryAcquireAsync("session-1", TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region RemoveSession Tests

    [Fact]
    public async Task RemoveSession_ExistingSession_RemovesLock()
    {
        // Acquire and release to create the session lock
        using (var handle = await _lock.AcquireAsync("session-1"))
        {
        }

        // Remove the session
        _lock.RemoveSession("session-1");

        // Should be able to acquire again (new semaphore)
        using var newHandle = await _lock.AcquireAsync("session-1");
        newHandle.ShouldNotBeNull();
    }

    [Fact]
    public void RemoveSession_NonExistentSession_NoError()
    {
        // Should not throw
        _lock.RemoveSession("nonexistent-session");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_Multiple_NoError()
    {
        var localLock = new GraphOperationLock();

        // Multiple dispose should not throw
        localLock.Dispose();
        localLock.Dispose();
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task AcquireAsync_Cancellation_ThrowsOperationCancelledException()
    {
        // Hold the lock
        using var hold = await _lock.AcquireAsync("session-1");

        // Try to acquire with already cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _lock.AcquireAsync("session-1", cts.Token));
    }

    [Fact]
    public async Task TryAcquireAsync_Cancellation_ThrowsOperationCancelledException()
    {
        // Hold the lock
        using var hold = await _lock.AcquireAsync("session-1");

        // Try to acquire with already cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _lock.TryAcquireAsync("session-1", TimeSpan.FromSeconds(10), cts.Token));
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentAcquires_SameSession_AllComplete()
    {
        var completedCount = 0;
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () =>
            {
                using var handle = await _lock.AcquireAsync("session-1");
                await Task.Delay(5);
                Interlocked.Increment(ref completedCount);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        completedCount.ShouldBe(10);
    }

    [Fact]
    public async Task MixedSessions_CorrectSerialization()
    {
        var session1Count = 0;
        var session2Count = 0;

        var tasks = new List<Task>();

        // 5 operations for session-1
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var handle = await _lock.AcquireAsync("session-1");
                await Task.Delay(5);
                Interlocked.Increment(ref session1Count);
            }));
        }

        // 5 operations for session-2
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var handle = await _lock.AcquireAsync("session-2");
                await Task.Delay(5);
                Interlocked.Increment(ref session2Count);
            }));
        }

        await Task.WhenAll(tasks);

        session1Count.ShouldBe(5);
        session2Count.ShouldBe(5);
    }

    #endregion
}
