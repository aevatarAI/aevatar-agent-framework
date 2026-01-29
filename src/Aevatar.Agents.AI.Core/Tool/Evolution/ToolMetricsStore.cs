using System.Collections.Concurrent;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  Tool Metrics Store (in-memory)
//
//  中文 + ASCII:
//  - best-effort：不阻塞主链路。
//  - 以 snapshot 周期聚合指标，供演化策略/观测使用。
// ============================================================
/// <summary>
/// In-memory tool metrics store (best-effort).
/// </summary>
public sealed class ToolMetricsStore
{
    private readonly ConcurrentDictionary<string, ToolMetricsCounter> _metrics = new();
    private long _totalCalls;
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;

    public ToolMetricsSnapshot? Record(ToolExecutionFeedback feedback, int snapshotEveryNCalls)
    {
        if (feedback == null)
            return null;

        var key = BuildKey(feedback.ToolName, feedback.ToolVersion);
        var counter = _metrics.GetOrAdd(key, _ => new ToolMetricsCounter(feedback.ToolName, feedback.ToolVersion));
        counter.Record(feedback);

        var total = Interlocked.Increment(ref _totalCalls);
        if (snapshotEveryNCalls <= 0 || total % snapshotEveryNCalls != 0)
            return null;

        return BuildSnapshot();
    }

    public double? GetFailureRate(string toolName, string toolVersion)
    {
        var key = BuildKey(toolName, toolVersion);
        if (!_metrics.TryGetValue(key, out var counter))
            return null;

        return counter.FailureRate;
    }

    public ToolMetricsEntry? GetEntry(string toolName, string toolVersion)
    {
        var key = BuildKey(toolName, toolVersion);
        if (!_metrics.TryGetValue(key, out var counter))
            return null;

        return counter.ToEntry();
    }

    public ToolMetricsSnapshot BuildSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new ToolMetricsSnapshot
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            Timestamp = Timestamp.FromDateTime(now.UtcDateTime),
            WindowStart = Timestamp.FromDateTime(_windowStart.UtcDateTime),
            WindowEnd = Timestamp.FromDateTime(now.UtcDateTime)
        };

        foreach (var counter in _metrics.Values)
        {
            snapshot.Entries.Add(counter.ToEntry());
        }

        _windowStart = now;
        return snapshot;
    }

    private static string BuildKey(string toolName, string toolVersion)
        => $"{toolName ?? string.Empty}@{toolVersion ?? string.Empty}".ToLowerInvariant();

    private sealed class ToolMetricsCounter
    {
        private readonly string _toolName;
        private readonly string _toolVersion;
        private long _totalCalls;
        private long _successCalls;
        private long _failureCalls;
        private long _totalDurationMs;

        public ToolMetricsCounter(string toolName, string toolVersion)
        {
            _toolName = toolName ?? string.Empty;
            _toolVersion = toolVersion ?? string.Empty;
        }

        public void Record(ToolExecutionFeedback feedback)
        {
            Interlocked.Increment(ref _totalCalls);
            if (feedback.Success)
                Interlocked.Increment(ref _successCalls);
            else
                Interlocked.Increment(ref _failureCalls);

            if (feedback.DurationMs > 0)
                Interlocked.Add(ref _totalDurationMs, feedback.DurationMs);
        }

        public double FailureRate
        {
            get
            {
                var total = Volatile.Read(ref _totalCalls);
                if (total <= 0)
                    return 0;
                var failures = Volatile.Read(ref _failureCalls);
                return (double)failures / total;
            }
        }

        public ToolMetricsEntry ToEntry()
        {
            var total = Volatile.Read(ref _totalCalls);
            var success = Volatile.Read(ref _successCalls);
            var failure = Volatile.Read(ref _failureCalls);
            var duration = Volatile.Read(ref _totalDurationMs);

            var avg = total > 0 ? duration / total : 0;
            var successRate = total > 0 ? (double)success / total : 0;

            return new ToolMetricsEntry
            {
                ToolName = _toolName,
                ToolVersion = _toolVersion,
                TotalCalls = total,
                SuccessCalls = success,
                FailureCalls = failure,
                SuccessRate = successRate,
                AvgDurationMs = avg
            };
        }
    }
}
