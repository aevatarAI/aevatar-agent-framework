using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Metrics instrumentation for pivot operations using .NET Meters API.
/// </summary>
public sealed class PivotMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly IPivotQueue _queue;

    private readonly Histogram<double> _pivotLatency;
    private readonly Counter<long> _pivotSuccessCount;
    private readonly Counter<long> _pivotFailureCount;
    private readonly Counter<long> _rollbackCount;
    private readonly Histogram<double> _detectionConfidence;
    private readonly ObservableGauge<int> _queueDepth;

    public const string MeterName = "VibeResearching.Pivot";

    public PivotMetrics(IPivotQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        _meter = new Meter(MeterName, "1.0.0");

        _pivotLatency = _meter.CreateHistogram<double>(
            "pivot.latency.ms",
            unit: "ms",
            description: "Latency of pivot operations in milliseconds");

        _pivotSuccessCount = _meter.CreateCounter<long>(
            "pivot.success.count",
            description: "Number of successful pivot operations");

        _pivotFailureCount = _meter.CreateCounter<long>(
            "pivot.failure.count",
            description: "Number of failed pivot operations");

        _rollbackCount = _meter.CreateCounter<long>(
            "pivot.rollback.count",
            description: "Number of rollback operations");

        _detectionConfidence = _meter.CreateHistogram<double>(
            "pivot.detection.confidence",
            description: "Confidence scores from pivot intent detection");

        _queueDepth = _meter.CreateObservableGauge(
            "pivot.queue.depth",
            () => GetQueueDepthMeasurements(),
            description: "Current depth of pivot queues per session");
    }

    /// <summary>
    /// Records the completion of a pivot operation.
    /// </summary>
    public void RecordPivotCompletion(string sessionId, bool success, double latencyMs)
    {
        var tags = new TagList
        {
            { "session_id", sessionId },
            { "success", success.ToString().ToLowerInvariant() }
        };

        _pivotLatency.Record(latencyMs, tags);

        if (success)
        {
            _pivotSuccessCount.Add(1, tags);
        }
        else
        {
            _pivotFailureCount.Add(1, tags);
        }
    }

    /// <summary>
    /// Records a detection confidence score.
    /// </summary>
    public void RecordDetectionConfidence(string sessionId, double confidence, bool triggered)
    {
        var tags = new TagList
        {
            { "session_id", sessionId },
            { "triggered", triggered.ToString().ToLowerInvariant() }
        };

        _detectionConfidence.Record(confidence, tags);
    }

    /// <summary>
    /// Records a rollback operation.
    /// </summary>
    public void RecordRollback(string sessionId, bool success)
    {
        var tags = new TagList
        {
            { "session_id", sessionId },
            { "success", success.ToString().ToLowerInvariant() }
        };

        _rollbackCount.Add(1, tags);
    }

    /// <summary>
    /// Creates a timer for measuring pivot operation latency.
    /// </summary>
    public PivotLatencyTimer StartLatencyTimer(string sessionId)
    {
        return new PivotLatencyTimer(this, sessionId);
    }

    private IEnumerable<Measurement<int>> GetQueueDepthMeasurements()
    {
        var statuses = _queue.GetAllQueueStatuses();

        foreach (var status in statuses)
        {
            yield return new Measurement<int>(
                status.QueueDepth,
                new TagList { { "session_id", status.SessionId } });
        }

        // If no sessions, return a zero measurement
        if (statuses.Count == 0)
        {
            yield return new Measurement<int>(0, new TagList { { "session_id", "none" } });
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    /// <summary>
    /// Helper class for timing pivot operations.
    /// </summary>
    public sealed class PivotLatencyTimer : IDisposable
    {
        private readonly PivotMetrics _metrics;
        private readonly string _sessionId;
        private readonly Stopwatch _stopwatch;
        private bool _success;
        private bool _disposed;

        internal PivotLatencyTimer(PivotMetrics metrics, string sessionId)
        {
            _metrics = metrics;
            _sessionId = sessionId;
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Marks the operation as successful before stopping.
        /// </summary>
        public void MarkSuccess()
        {
            _success = true;
        }

        /// <summary>
        /// Marks the operation as failed before stopping.
        /// </summary>
        public void MarkFailure()
        {
            _success = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            _metrics.RecordPivotCompletion(_sessionId, _success, _stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
