using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

/// <summary>
/// Orleans-based EventStore implementation (Simplified)
/// 
/// Architecture:
/// - Events: Stored via IEventRepository directly (no extra Grain RPC)
/// - Snapshots: Handled by GAgentBase via IStateStore (not in this class)
/// - Concurrency: Orleans Grain guarantees single-thread execution per Agent
/// 
/// Optimization: Removed EventStorageGrain dependency to reduce RPC overhead.
/// Concurrency is guaranteed by:
/// 1. OrleansGAgentGrain single-threaded execution per Agent
/// 2. MongoDB optimistic locking in repository layer (as backup)
/// </summary>
public class OrleansEventStore : IEventStore
{
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<OrleansEventStore> _logger;

    public OrleansEventStore(
        IEventRepository eventRepository,
        ILogger<OrleansEventStore> logger)
    {
        _eventRepository = eventRepository;
        _logger = logger;
    }

    // ========== Event Operations ==========

    public async Task<long> AppendEventsAsync(
        string agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            return expectedVersion;
        }

        // Optimistic concurrency check (defensive programming, even though Grain guarantees single-thread execution)
        var currentVersion = await _eventRepository.GetLatestVersionAsync(agentId, agentTypeName, ct);
        if (currentVersion != expectedVersion)
        {
            // Special case: Migration scenario - snapshot exists (expectedVersion > 0) but event stream is empty (currentVersion == 0)
            // This is normal after data migration where only snapshots are imported without event history
            if (currentVersion == 0 && expectedVersion > 0)
            {
                _logger.LogInformation(
                    "Migration scenario detected for agent {AgentId}: snapshot version {SnapshotVersion}, event stream empty. Allowing append.",
                    agentId, expectedVersion);
                // Continue execution - this is expected after migration
            }
            else
            {
                _logger.LogWarning(
                    "Version conflict for agent {AgentId}: expected {ExpectedVersion}, got {CurrentVersion}",
                    agentId, expectedVersion, currentVersion);
                throw new InvalidOperationException(
                    $"Concurrency conflict: expected version {expectedVersion}, got {currentVersion}");
            }
        }

        // Direct repository call - no extra Grain RPC
        // Concurrency is guaranteed by OrleansGAgentGrain single-threaded execution
        var newVersion = await _eventRepository.AppendEventsAsync(
            agentId, 
            eventList, 
            agentTypeName, 
            ct);

        _logger.LogDebug(
            "Appended {EventCount} events for agent {AgentId} (type: {AgentType}), version: {OldVersion} -> {NewVersion}",
            eventList.Count, agentId, agentTypeName ?? "unknown", expectedVersion, newVersion);

        return newVersion;
    }

    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        string agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        return await _eventRepository.GetEventsAsync(
            agentId, 
            fromVersion, 
            toVersion, 
            maxCount, 
            agentTypeName, 
            ct);
    }

    public async Task<long> GetLatestVersionAsync(
        string agentId, 
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        return await _eventRepository.GetLatestVersionAsync(agentId, agentTypeName, ct);
    }

    // Note: Snapshots are handled by IStateStore<TState> in GAgentBase (per-type collections)
}
