using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Sessions.Repositories;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Repositories;

/// <summary>
/// File-based implementation of IVibeSessionRepository.
/// Uses JSON file for persistence (alternative to MongoDB).
/// </summary>
internal sealed class FileVibeSessionRepository : IVibeSessionRepository
{
    private static readonly JsonFormatter Formatter = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true));

    private static readonly JsonParser Parser = new(
        JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true));

    private readonly string _indexPath;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly ILogger<FileVibeSessionRepository>? _logger;

    public FileVibeSessionRepository(IHostEnvironment env, ILogger<FileVibeSessionRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        _logger = logger;
        _indexPath = ResolveIndexPath(env.ContentRootPath);
    }

    public async Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return null;

        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            return index.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task SaveAsync(VibeSessionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var sessionId = VibeSessionStoreHelpers.NormalizeSessionId(record.SessionId);
        if (sessionId.Length == 0)
            throw new ArgumentException("session_id is required.", nameof(record));

        record.SessionId = sessionId;

        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var existing = index.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(record.ProviderName))
                    record.ProviderName = existing.ProviderName;
                if (string.IsNullOrWhiteSpace(record.DagId))
                    record.DagId = existing.DagId;
                if (string.IsNullOrWhiteSpace(record.CoordinatorId))
                    record.CoordinatorId = existing.CoordinatorId;

                VibeSessionStoreHelpers.MergeList(record.AgentIds, existing.AgentIds);
                VibeSessionStoreHelpers.MergeList(record.WorkerIds, existing.WorkerIds);

                if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.CreatedAt))
                    record.CreatedAt = existing.CreatedAt;
            }

            if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.CreatedAt))
                record.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            if (VibeSessionStoreHelpers.IsDefaultTimestamp(record.UpdatedAt))
                record.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            if (existing == null)
            {
                index.Sessions.Add(record);
            }
            else
            {
                existing.ProviderName = record.ProviderName;
                existing.DagId = record.DagId;
                existing.CoordinatorId = record.CoordinatorId;
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

    public async Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return false;

        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            return index.Sessions.Any(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<IReadOnlyList<VibeSessionRecord>> ListAsync(CancellationToken ct = default)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            return index.Sessions.ToList();
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        sessionId = VibeSessionStoreHelpers.NormalizeSessionId(sessionId);
        if (sessionId.Length == 0)
            return;

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
        try
        {
            if (!File.Exists(_indexPath))
                return new VibeSessionIndex();

            var json = await File.ReadAllTextAsync(_indexPath, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new VibeSessionIndex();

            return Parser.Parse<VibeSessionIndex>(json) ?? new VibeSessionIndex();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load vibe session index (file-backed).");
            return new VibeSessionIndex();
        }
    }

    private async Task SaveIndexAsync(VibeSessionIndex index, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_indexPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var tmp = Path.Combine(dir ?? ".", $"{Guid.NewGuid():N}.tmp");
            var json = Formatter.Format(index);
            await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            File.Move(tmp, _indexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save vibe session index (file-backed).");
        }
    }

    private static string ResolveIndexPath(string contentRoot)
    {
        var systemRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        var dataDir = Path.Combine(systemRoot, "workspace", ".data");
        return Path.Combine(dataDir, "vibe_sessions.json");
    }
}
