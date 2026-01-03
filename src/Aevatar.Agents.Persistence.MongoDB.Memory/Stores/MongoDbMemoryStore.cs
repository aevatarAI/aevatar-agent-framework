using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.MongoDB.Memory.Documents;
using Aevatar.Agents.Persistence.MongoDB.Memory.Options;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.Memory.Stores;

/// <summary>
/// MongoDB-backed IMemoryStore implementation.
/// </summary>
public sealed class MongoDbMemoryStore : IMemoryStore
{
    private readonly IMongoCollection<MemoryEntryDocument> _entries;
    private readonly MongoDbMemoryOptions _options;

    public MongoDbMemoryStore(IMongoDatabase database, IOptions<MongoDbMemoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        var collectionName = string.IsNullOrWhiteSpace(_options.MemoryEntriesCollection)
            ? "memory_entries"
            : _options.MemoryEntriesCollection.Trim();

        _entries = database.GetCollection<MemoryEntryDocument>(collectionName);

        EnsureIndexes();
    }

    public async Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.MemoryId))
            throw new ArgumentException("MemoryEntry.memory_id is required.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.EntryId))
            throw new ArgumentException("MemoryEntry.entry_id is required.", nameof(entry));

        var memoryId = entry.MemoryId.Trim();
        var entryId = entry.EntryId.Trim();

        var scopeType = (int)(entry.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = entry.Scope?.ScopeId ?? string.Empty;

        var doc = new MemoryEntryDocument
        {
            Id = $"{memoryId}::{entryId}",
            MemoryId = memoryId,
            EntryId = entryId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            RunId = entry.RunId ?? string.Empty,
            AgentId = entry.AgentId ?? string.Empty,
            Role = entry.Role ?? string.Empty,
            Content = entry.Content ?? string.Empty,
            Tags = entry.Tags.ToDictionary(k => k.Key, v => v.Value ?? string.Empty, StringComparer.Ordinal),
            CreatedAt = NormalizeCreatedAtUtc(entry.CreatedAt)
        };

        // Append-only semantics: ignore duplicates.
        try
        {
            await _entries.InsertOneAsync(doc, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ignore
        }
    }

    public async Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            return Array.Empty<MemoryResourceSummary>();

        var filter = Builders<MemoryEntryDocument>.Filter.Empty;
        if (scopeTypeFilter.HasValue && scopeTypeFilter.Value != MemoryScopeType.Unspecified)
        {
            filter &= Builders<MemoryEntryDocument>.Filter.Eq(x => x.ScopeType, (int)scopeTypeFilter.Value);
        }

        // Group by memory_id + scope
        var pipeline = _entries.Aggregate()
            .Match(filter)
            .Group(
                x => new { x.MemoryId, x.ScopeType, x.ScopeId },
                g => new
                {
                    g.Key.MemoryId,
                    g.Key.ScopeType,
                    g.Key.ScopeId,
                    EntryCount = g.Count(),
                    LatestAt = g.Max(x => x.CreatedAt)
                })
            .SortByDescending(x => x.LatestAt)
            .Limit(limit);

        var rows = await pipeline.ToListAsync(ct).ConfigureAwait(false);

        return rows
            .Select(r => new MemoryResourceSummary
            {
                MemoryId = r.MemoryId,
                Scope = new MemoryScope
                {
                    Type = (MemoryScopeType)r.ScopeType,
                    ScopeId = r.ScopeId ?? string.Empty
                },
                EntryCount = r.EntryCount,
                LatestAt = Timestamp.FromDateTime((r.LatestAt.Kind == DateTimeKind.Utc ? r.LatestAt : r.LatestAt.ToUniversalTime()))
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
            throw new ArgumentException("memoryId cannot be null/empty.", nameof(memoryId));

        if (limit <= 0)
            return Array.Empty<MemoryEntry>();

        memoryId = memoryId.Trim();

        var filter = Builders<MemoryEntryDocument>.Filter.Eq(x => x.MemoryId, memoryId);
        var docs = await _entries
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return docs.Select(ToMemoryEntry).ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MemoryEntry>();

        if (limit <= 0)
            return Array.Empty<MemoryEntry>();

        query = query.Trim();
        memoryId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim();

        var filter = Builders<MemoryEntryDocument>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(memoryId))
        {
            filter &= Builders<MemoryEntryDocument>.Filter.Eq(x => x.MemoryId, memoryId);
        }

        if (scopeTypeFilter.HasValue && scopeTypeFilter.Value != MemoryScopeType.Unspecified)
        {
            filter &= Builders<MemoryEntryDocument>.Filter.Eq(x => x.ScopeType, (int)scopeTypeFilter.Value);
        }

        // Simple substring search (case-insensitive). For production, prefer Atlas Search / text index.
        filter &= Builders<MemoryEntryDocument>.Filter.Regex(
            x => x.Content,
            new BsonRegularExpression(query, "i"));

        var docs = await _entries
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return docs.Select(ToMemoryEntry).ToList();
    }

    private void EnsureIndexes()
    {
        // Unique key on Id (BsonId) => _id index exists and is unique.

        // Common access patterns:
        // - list by memoryId, sort by createdAt
        var idx1 = new CreateIndexModel<MemoryEntryDocument>(
            Builders<MemoryEntryDocument>.IndexKeys
                .Ascending(x => x.MemoryId)
                .Descending(x => x.CreatedAt));
        _entries.Indexes.CreateOne(idx1);

        if (_options.EnsureTextIndexOnContent)
        {
            var idx = new CreateIndexModel<MemoryEntryDocument>(
                Builders<MemoryEntryDocument>.IndexKeys.Text(x => x.Content));
            _entries.Indexes.CreateOne(idx);
        }
    }

    private static DateTime NormalizeCreatedAtUtc(Timestamp? createdAt)
    {
        if (createdAt is null || (createdAt.Seconds == 0 && createdAt.Nanos == 0))
            return DateTime.UtcNow;

        var dt = createdAt.ToDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }

    private static MemoryEntry ToMemoryEntry(MemoryEntryDocument doc)
    {
        var entry = new MemoryEntry
        {
            EntryId = doc.EntryId ?? string.Empty,
            MemoryId = doc.MemoryId ?? string.Empty,
            Scope = new MemoryScope
            {
                Type = (MemoryScopeType)doc.ScopeType,
                ScopeId = doc.ScopeId ?? string.Empty
            },
            RunId = doc.RunId ?? string.Empty,
            AgentId = doc.AgentId ?? string.Empty,
            Role = doc.Role ?? string.Empty,
            Content = doc.Content ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime((doc.CreatedAt.Kind == DateTimeKind.Utc ? doc.CreatedAt : doc.CreatedAt.ToUniversalTime()))
        };

        if (doc.Tags != null)
        {
            foreach (var kv in doc.Tags)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key))
                    entry.Tags[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        return entry;
    }
}


