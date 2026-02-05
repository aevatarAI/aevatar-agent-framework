using System.Collections.Concurrent;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;

/// <summary>
/// Manages per-user semaphore locks for Codex token refresh serialization.
/// Uses ConcurrentDictionary with periodic eviction of stale entries
/// to prevent unbounded memory growth.
/// </summary>
internal static class RefreshLockManager
{
    private static readonly ConcurrentDictionary<Guid, LockEntry> Locks = new();
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(30);
    private static long _lastEvictionTicks = DateTimeOffset.UtcNow.Ticks;

    /// <summary>
    /// Gets or creates a per-user SemaphoreSlim, updating the last-access timestamp.
    /// Periodically evicts entries that have not been accessed recently.
    /// </summary>
    public static SemaphoreSlim GetOrCreate(Guid userId)
    {
        EvictStaleEntries();

        var entry = Locks.GetOrAdd(userId, _ => new LockEntry());
        entry.LastAccessedTicks = DateTimeOffset.UtcNow.Ticks;
        return entry.Semaphore;
    }

    private static void EvictStaleEntries()
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastEvictionTicks);

        if (nowTicks - lastTicks < EvictionInterval.Ticks)
            return;

        // Only one thread performs eviction at a time
        if (Interlocked.CompareExchange(ref _lastEvictionTicks, nowTicks, lastTicks) != lastTicks)
            return;

        var threshold = nowTicks - EvictionInterval.Ticks;
        foreach (var kvp in Locks)
        {
            if (Interlocked.Read(ref kvp.Value.LastAccessedTicks) < threshold)
            {
                Locks.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public long LastAccessedTicks = DateTimeOffset.UtcNow.Ticks;
    }
}
