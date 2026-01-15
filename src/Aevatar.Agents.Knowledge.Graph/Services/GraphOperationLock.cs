using System.Collections.Concurrent;

namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Provides session-scoped locking for serializing DAG operations.
/// Uses SemaphoreSlim per session to prevent race conditions when
/// multiple agents modify the graph concurrently.
/// </summary>
public sealed class GraphOperationLock : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private bool _disposed;

    /// <summary>
    /// Acquires a lock for the specified session.
    /// The returned IDisposable releases the lock when disposed.
    /// </summary>
    /// <param name="sessionId">The session ID to lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable that releases the lock when disposed.</returns>
    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var semaphore = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockReleaser(semaphore);
    }

    /// <summary>
    /// Tries to acquire a lock for the specified session within the given timeout.
    /// </summary>
    /// <param name="sessionId">The session ID to lock.</param>
    /// <param name="timeout">Maximum time to wait for the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable that releases the lock, or null if timeout elapsed.</returns>
    public async Task<IDisposable?> TryAcquireAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var semaphore = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(timeout, cancellationToken);
        return acquired ? new LockReleaser(semaphore) : null;
    }

    /// <summary>
    /// Removes the lock for a session (call when session is ended/abandoned).
    /// </summary>
    /// <param name="sessionId">The session ID to remove.</param>
    public void RemoveSession(string sessionId)
    {
        if (_sessionLocks.TryRemove(sessionId, out var semaphore))
        {
            semaphore.Dispose();
        }
    }

    /// <summary>
    /// Disposes all session locks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _sessionLocks)
        {
            kvp.Value.Dispose();
        }
        _sessionLocks.Clear();
    }

    private sealed class LockReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            semaphore.Release();
        }
    }
}
