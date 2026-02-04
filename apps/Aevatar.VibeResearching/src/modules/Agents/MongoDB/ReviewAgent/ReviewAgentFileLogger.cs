using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.ReviewAgent;

namespace Aevatar.VibeResearching.Agents.MongoDB.ReviewAgent;

/// <summary>
/// File logger for Review Agent operations.
/// Writes structured logs to workspace/review-agent/logs/review.log
/// </summary>
/// <remarks>
/// This is a simple file logger that supplements the standard logging infrastructure.
/// Logs are buffered and flushed periodically or when the buffer is full.
/// Task T089.
/// </remarks>
public sealed class ReviewAgentFileLogger : IDisposable
{
    private readonly string _logFilePath;
    private readonly ILogger<ReviewAgentFileLogger> _logger;
    private readonly ConcurrentQueue<string> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly int _maxBufferSize = 100;
    private bool _disposed;

    public ReviewAgentFileLogger(
        string workspaceDirectory,
        ILogger<ReviewAgentFileLogger> logger)
    {
        _logger = logger;

        // Create logs directory
        var logsDir = Path.Combine(workspaceDirectory, "review-agent", "logs");
        Directory.CreateDirectory(logsDir);

        _logFilePath = Path.Combine(logsDir, "review.log");

        // Flush every 5 seconds or when buffer is full
        _flushTimer = new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger.LogDebug("ReviewAgentFileLogger initialized, writing to {Path}", _logFilePath);
    }

    /// <summary>
    /// Log a review round start event.
    /// </summary>
    public void LogReviewRoundStart(string iterationId, int nodesToReview)
    {
        Log("REVIEW_START", new
        {
            iteration_id = iterationId,
            nodes_to_review = nodesToReview
        });
    }

    /// <summary>
    /// Log a review round completion event.
    /// </summary>
    public void LogReviewRoundComplete(string iterationId, int reviewed, int valid, int deactivated, TimeSpan duration)
    {
        Log("REVIEW_COMPLETE", new
        {
            iteration_id = iterationId,
            nodes_reviewed = reviewed,
            nodes_valid = valid,
            nodes_deactivated = deactivated,
            duration_ms = (long)duration.TotalMilliseconds
        });
    }

    /// <summary>
    /// Log a node review event.
    /// </summary>
    public void LogNodeReview(string iterationId, string nodeId, ReviewResult result, string? reason = null)
    {
        Log("NODE_REVIEW", new
        {
            iteration_id = iterationId,
            node_id = nodeId,
            result = result.ToString(),
            reason
        });
    }

    /// <summary>
    /// Log a node deactivation event.
    /// </summary>
    public void LogNodeDeactivated(string nodeId, string reason)
    {
        Log("NODE_DEACTIVATED", new
        {
            node_id = nodeId,
            reason
        });
    }

    /// <summary>
    /// Log a cleanup round event.
    /// </summary>
    public void LogCleanupRound(int nodesRemoved)
    {
        Log("CLEANUP_COMPLETE", new
        {
            nodes_removed = nodesRemoved
        });
    }

    /// <summary>
    /// Log a status change event.
    /// </summary>
    public void LogStatusChange(ReviewAgentStatus oldStatus, ReviewAgentStatus newStatus)
    {
        Log("STATUS_CHANGE", new
        {
            old_status = oldStatus.ToString(),
            new_status = newStatus.ToString()
        });
    }

    /// <summary>
    /// Log an error event.
    /// </summary>
    public void LogError(string context, string error, Exception? ex = null)
    {
        Log("ERROR", new
        {
            context,
            error,
            exception_type = ex?.GetType().Name,
            stack_trace = ex?.StackTrace?.Split('\n').Take(5).ToArray()
        });
    }

    /// <summary>
    /// Log a settings change event.
    /// </summary>
    public void LogSettingsChange(ReviewAgentOptions oldSettings, ReviewAgentOptions newSettings)
    {
        Log("SETTINGS_CHANGE", new
        {
            iteration_interval_minutes = new { old = oldSettings.IterationIntervalMinutes, @new = newSettings.IterationIntervalMinutes },
            out_of_date_threshold_minutes = new { old = oldSettings.OutOfDateThresholdMinutes, @new = newSettings.OutOfDateThresholdMinutes },
            to_delete_threshold_minutes = new { old = oldSettings.ToDeleteThresholdMinutes, @new = newSettings.ToDeleteThresholdMinutes },
            llm_provider = new { old = oldSettings.LLMProviderName, @new = newSettings.LLMProviderName }
        });
    }

    private void Log(string eventType, object data)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var line = $"{timestamp} [{eventType}] {json}";

        _buffer.Enqueue(line);

        // Flush if buffer is getting full
        if (_buffer.Count >= _maxBufferSize)
        {
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        if (_disposed) return;
        if (_buffer.IsEmpty) return;

        if (!await _writeLock.WaitAsync(100))
        {
            return; // Skip if another flush is in progress
        }

        try
        {
            var sb = new StringBuilder();
            while (_buffer.TryDequeue(out var line))
            {
                sb.AppendLine(line);
            }

            if (sb.Length > 0)
            {
                await File.AppendAllTextAsync(_logFilePath, sb.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write to Review Agent log file");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();

        // Final flush
        FlushAsync().GetAwaiter().GetResult();

        _writeLock.Dispose();
    }
}
