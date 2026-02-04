using System.Text;
using Aevatar.Agents.Abstractions.Persistence; // TODO: Reference old namespace until Agents module migrated
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Sessions.Repositories;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Repositories;

/// <summary>
/// MongoDB implementation of IVibeSessionRepository.
/// Uses IStateStore abstraction for persistence.
/// </summary>
internal sealed class MongoVibeSessionRepository : IVibeSessionRepository
{
    private const string IndexKey = "vibe_researching_sessions_index";

    private readonly IStateStore<VibeSessionIndex> _indexStore;
    private readonly IStateStore<VibeSessionRecord> _recordStore;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public MongoVibeSessionRepository(
        IStateStore<VibeSessionIndex> indexStore,
        IStateStore<VibeSessionRecord> recordStore)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
    }

    public async Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return null;

        var record = await _recordStore.LoadAsync(sessionId, ct);
        if (record != null)
            return record;

        var index = await LoadIndexAsync(ct);
        return index.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
    }

    public async Task SaveAsync(VibeSessionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var sessionId = VibeSessionStoreHelpers.NormalizeSessionId(record.SessionId);
        if (sessionId.Length == 0)
            throw new ArgumentException("session_id is required.", nameof(record));

        record.SessionId = sessionId;
        var existing = await _recordStore.LoadAsync(sessionId, ct);
        if (existing == null)
        {
            var index = await LoadIndexAsync(ct);
            existing = index.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        }

        if (existing != null)
        {
            if (string.IsNullOrWhiteSpace(record.ProviderName))
                record.ProviderName = existing.ProviderName;
            if (string.IsNullOrWhiteSpace(record.DagId))
                record.DagId = existing.DagId;
            if (string.IsNullOrWhiteSpace(record.CoordinatorId))
                record.CoordinatorId = existing.CoordinatorId;
            if (string.IsNullOrWhiteSpace(record.OwnerId))
                record.OwnerId = existing.OwnerId;
            if (string.IsNullOrWhiteSpace(record.OwnerName))
                record.OwnerName = existing.OwnerName;

            VibeSessionStoreHelpers.MergeList(record.AgentIds, existing.AgentIds);
            VibeSessionStoreHelpers.MergeList(record.WorkerIds, existing.WorkerIds);

            if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.CreatedAt))
                record.CreatedAt = existing.CreatedAt;
        }

        if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.CreatedAt))
            record.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.UpdatedAt))
            record.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        await _recordStore.SaveAsync(sessionId, record, ct);
        await UpsertIndexEntryAsync(record, ct);
    }

    public async Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return false;

        if (await _recordStore.ExistsAsync(sessionId, ct))
            return true;

        var index = await LoadIndexAsync(ct);
        return index.Sessions.Any(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<VibeSessionRecord>> ListAsync(CancellationToken ct = default)
    {
        var index = await LoadIndexAsync(ct);
        return index.Sessions.ToList();
    }

    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return;

        // Delete from record store
        await _recordStore.DeleteAsync(sessionId, ct);

        // Remove from index
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var existing = index.Sessions
                .FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

            if (existing != null)
            {
                index.Sessions.Remove(existing);
                await SaveIndexAsync(index, ct);
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<VibeSessionIndex> LoadIndexAsync(CancellationToken ct)
    {
        var loaded = await _indexStore.LoadAsync(IndexKey, ct);
        return loaded ?? new VibeSessionIndex();
    }

    private async Task SaveIndexAsync(VibeSessionIndex index, CancellationToken ct)
    {
        await _indexStore.SaveAsync(IndexKey, index, ct);
    }

    private async Task UpsertIndexEntryAsync(VibeSessionRecord record, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var existing = index.Sessions
                .FirstOrDefault(s => string.Equals(s.SessionId, record.SessionId, StringComparison.Ordinal));

            if (existing == null)
            {
                index.Sessions.Add(record);
            }
            else
            {
                existing.ProviderName = record.ProviderName;
                existing.DagId = record.DagId;
                existing.CoordinatorId = record.CoordinatorId;
                existing.OwnerId = record.OwnerId;
                existing.OwnerName = record.OwnerName;
                VibeSessionStoreHelpers.MergeList(existing.AgentIds, record.AgentIds);
                VibeSessionStoreHelpers.MergeList(existing.WorkerIds, record.WorkerIds);
                existing.CreatedAt = record.CreatedAt;
                existing.UpdatedAt = record.UpdatedAt;
                // Lifecycle status fields
                existing.Status = record.Status;
                existing.PausedAt = record.PausedAt;
                existing.ArchivedAt = record.ArchivedAt;
                existing.LastUserMessage = record.LastUserMessage;
                existing.LastRunMode = record.LastRunMode;
            }

            await SaveIndexAsync(index, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }
}

/// <summary>
/// Helper utilities for session ID normalization and timestamp handling.
/// </summary>
internal static class VibeSessionStoreHelpers
{
    internal static string NormalizeSessionId(string? sessionId)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            return string.Empty;

        if (sessionId.Length > 64)
            sessionId = sessionId[..64];

        var normalized = sessionId.ToLowerInvariant();
        foreach (var ch in normalized)
        {
            if (!char.IsLetterOrDigit(ch))
                throw new ArgumentException("sessionId must be alphanumeric", nameof(sessionId));
        }

        return normalized;
    }

    internal static bool IsDefaultTimestamp(Timestamp? ts)
        => ts == null || (ts.Seconds == 0 && ts.Nanos == 0);

    internal static void MergeList(RepeatedField<string> target, IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            var value = (item ?? string.Empty).Trim();
            if (value.Length == 0)
                continue;
            if (!target.Contains(value))
                target.Add(value);
        }
    }
}
