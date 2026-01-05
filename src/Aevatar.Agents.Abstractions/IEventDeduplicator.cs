using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Event deduplicator interface
/// Provides time-window-based event deduplication mechanism
/// </summary>
public interface IEventDeduplicator
{
    /// <summary>
    /// Try to record event
    /// </summary>
    /// <param name="eventId">Event ID</param>
    /// <returns>Returns true if new event, false if duplicate</returns>
    Task<bool> TryRecordEventAsync(string eventId);

    /// <summary>
    /// Batch record events
    /// </summary>
    /// <param name="eventIds">Event ID list</param>
    /// <returns>New event ID list</returns>
    Task<IReadOnlyList<string>> TryRecordEventsAsync(IReadOnlyList<string> eventIds);

    /// <summary>
    /// Check if event has been processed
    /// </summary>
    /// <param name="eventId">Event ID</param>
    /// <returns>Whether processed</returns>
    Task<bool> IsProcessedAsync(string eventId);

    /// <summary>
    /// Clean up expired event records
    /// </summary>
    /// <returns>Number of records cleaned</returns>
    Task<int> CleanupExpiredAsync();

    /// <summary>
    /// Get statistics
    /// </summary>
    /// <returns>Deduplication statistics</returns>
    Task<DeduplicationStatistics> GetStatisticsAsync();

    /// <summary>
    /// Reset deduplicator
    /// </summary>
    Task ResetAsync();
}

/// <summary>
/// Deduplication statistics
/// </summary>
public class DeduplicationStatistics
{
    /// <summary>
    /// Total event count
    /// </summary>
    public long TotalEvents { get; set; }

    /// <summary>
    /// Duplicate event count
    /// </summary>
    public long DuplicateEvents { get; set; }

    /// <summary>
    /// Unique event count
    /// </summary>
    public long UniqueEvents { get; set; }

    /// <summary>
    /// Current cached event count
    /// </summary>
    public long CachedEvents { get; set; }

    /// <summary>
    /// Memory usage (bytes)
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Last cleanup time
    /// </summary>
    public DateTime? LastCleanupTime { get; set; }

    /// <summary>
    /// Duplicate rate
    /// </summary>
    public double DuplicateRate => TotalEvents > 0 ? (double)DuplicateEvents / TotalEvents : 0;
}

/// <summary>
/// Deduplication options
/// </summary>
public class DeduplicationOptions
{
    /// <summary>
    /// Event expiration time (default 5 minutes)
    /// </summary>
    public TimeSpan EventExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum cached event count (default 100000)
    /// </summary>
    public int MaxCachedEvents { get; set; } = 100_000;

    /// <summary>
    /// Auto cleanup interval (default 1 minute)
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether to enable auto cleanup (default true)
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// Cleanup batch size (default 1000)
    /// </summary>
    public int CleanupBatchSize { get; set; } = 1000;
}

