using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Agents.Pivot.Models;

namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// Manages queuing of pivot requests per session to prevent concurrent modifications.
/// Implements FR-012: serialized pivot processing per session.
/// </summary>
public sealed class PivotQueue : IPivotQueue
{
    private readonly ConcurrentDictionary<string, SessionPivotQueue> _sessionQueues = new();
    private readonly PivotOptions _options;
    private readonly ILogger<PivotQueue> _logger;

    public PivotQueue(
        IOptions<PivotOptions> options,
        ILogger<PivotQueue> logger)
    {
        _options = options?.Value ?? new PivotOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PivotQueueResult> EnqueueAsync(
        DirectionChangeIntent intent,
        Func<CancellationToken, Task<PivotOperation>> pivotExecutor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(pivotExecutor);

        var sessionQueue = _sessionQueues.GetOrAdd(
            intent.SessionId,
            sid => new SessionPivotQueue(sid, _options.MaxQueueDepth, _logger));

        return await sessionQueue.EnqueueAsync(intent, pivotExecutor, cancellationToken);
    }

    /// <inheritdoc />
    public int GetQueueDepth(string sessionId)
    {
        return _sessionQueues.TryGetValue(sessionId, out var queue)
            ? queue.CurrentDepth
            : 0;
    }

    /// <inheritdoc />
    public bool IsProcessing(string sessionId)
    {
        return _sessionQueues.TryGetValue(sessionId, out var queue) && queue.IsProcessing;
    }

    /// <inheritdoc />
    public IReadOnlyList<PivotQueueStatus> GetAllQueueStatuses()
    {
        return _sessionQueues.Values
            .Select(q => q.GetStatus())
            .ToList();
    }

    /// <inheritdoc />
    public void CleanupSession(string sessionId)
    {
        if (_sessionQueues.TryRemove(sessionId, out var queue))
        {
            queue.Dispose();
            _logger.LogDebug("Cleaned up pivot queue for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Per-session queue that serializes pivot requests.
    /// </summary>
    private sealed class SessionPivotQueue : IDisposable
    {
        private readonly string _sessionId;
        private readonly int _maxDepth;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly ConcurrentQueue<QueuedPivot> _pendingQueue = new();

        private volatile bool _isProcessing;
        private volatile int _currentDepth;
        private string? _currentPivotId;

        public SessionPivotQueue(string sessionId, int maxDepth, ILogger logger)
        {
            _sessionId = sessionId;
            _maxDepth = maxDepth;
            _logger = logger;
        }

        public bool IsProcessing => _isProcessing;
        public int CurrentDepth => _currentDepth;

        public async Task<PivotQueueResult> EnqueueAsync(
            DirectionChangeIntent intent,
            Func<CancellationToken, Task<PivotOperation>> pivotExecutor,
            CancellationToken cancellationToken)
        {
            // Check queue depth
            if (_currentDepth >= _maxDepth)
            {
                _logger.LogWarning(
                    "Pivot queue full for session {SessionId} (depth={Depth}, max={Max})",
                    _sessionId, _currentDepth, _maxDepth);

                return new PivotQueueResult
                {
                    SessionId = _sessionId,
                    Queued = false,
                    Position = -1,
                    QueueFull = true,
                    ErrorMessage = $"Pivot queue is full (max {_maxDepth} pending requests)"
                };
            }

            var position = Interlocked.Increment(ref _currentDepth);

            _logger.LogInformation(
                "Pivot request queued for session {SessionId}, position={Position}",
                _sessionId, position);

            // Try to acquire the semaphore (serialize pivot execution)
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                _isProcessing = true;
                _currentPivotId = !string.IsNullOrWhiteSpace(intent.PivotId)
                    ? intent.PivotId!.Trim()
                    : $"pivot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                // Execute the pivot
                var operation = await pivotExecutor(cancellationToken);

                return new PivotQueueResult
                {
                    SessionId = _sessionId,
                    Queued = position > 1,
                    Position = 0, // Now executing
                    Operation = operation
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pivot execution failed for session {SessionId}", _sessionId);
                return new PivotQueueResult
                {
                    SessionId = _sessionId,
                    Queued = position > 1,
                    Position = 0,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                _isProcessing = false;
                _currentPivotId = null;
                Interlocked.Decrement(ref _currentDepth);
                _semaphore.Release();
            }
        }

        public PivotQueueStatus GetStatus()
        {
            return new PivotQueueStatus
            {
                SessionId = _sessionId,
                IsProcessing = _isProcessing,
                CurrentPivotId = _currentPivotId,
                QueueDepth = _currentDepth,
                MaxDepth = _maxDepth
            };
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }

    private sealed record QueuedPivot(
        DirectionChangeIntent Intent,
        Func<CancellationToken, Task<PivotOperation>> Executor,
        TaskCompletionSource<PivotQueueResult> Completion);
}

/// <summary>
/// Interface for the pivot queue service.
/// </summary>
public interface IPivotQueue
{
    /// <summary>
    /// Enqueues a pivot request for execution.
    /// If another pivot is in progress for the same session, this request is queued.
    /// </summary>
    Task<PivotQueueResult> EnqueueAsync(
        DirectionChangeIntent intent,
        Func<CancellationToken, Task<PivotOperation>> pivotExecutor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current queue depth for a session.
    /// </summary>
    int GetQueueDepth(string sessionId);

    /// <summary>
    /// Checks if a pivot is currently being processed for a session.
    /// </summary>
    bool IsProcessing(string sessionId);

    /// <summary>
    /// Gets status for all session queues.
    /// </summary>
    IReadOnlyList<PivotQueueStatus> GetAllQueueStatuses();

    /// <summary>
    /// Cleans up resources for a session.
    /// </summary>
    void CleanupSession(string sessionId);
}

/// <summary>
/// Result of a pivot queue operation.
/// </summary>
public sealed class PivotQueueResult
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>True if the request was queued (not immediately executed).</summary>
    public bool Queued { get; init; }

    /// <summary>Position in queue (0 = currently executing, >0 = waiting).</summary>
    public int Position { get; init; }

    /// <summary>True if the queue is full and the request was rejected.</summary>
    public bool QueueFull { get; init; }

    /// <summary>The pivot operation result (if completed).</summary>
    public PivotOperation? Operation { get; init; }

    /// <summary>Error message if the request failed.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of a session's pivot queue.
/// </summary>
public sealed class PivotQueueStatus
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>True if a pivot is currently being processed.</summary>
    public bool IsProcessing { get; init; }

    /// <summary>ID of the pivot currently being processed (if any).</summary>
    public string? CurrentPivotId { get; init; }

    /// <summary>Number of pivot requests in queue.</summary>
    public int QueueDepth { get; init; }

    /// <summary>Maximum queue depth allowed.</summary>
    public int MaxDepth { get; init; }
}
