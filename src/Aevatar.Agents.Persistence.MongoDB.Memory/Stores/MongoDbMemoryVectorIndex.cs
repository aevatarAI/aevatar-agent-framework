using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.MongoDB.Memory.Documents;
using Aevatar.Agents.Persistence.MongoDB.Memory.Internal;
using Aevatar.Agents.Persistence.MongoDB.Memory.Options;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.Memory.Stores;

/// <summary>
/// MongoDB-backed IMemoryVectorIndex implementation.
///
/// Note:
/// - This baseline implementation does brute-force cosine similarity in-process after MongoDB filtering.
/// - For production, you can swap to MongoDB Atlas Vector Search ($vectorSearch) when available.
/// </summary>
public sealed class MongoDbMemoryVectorIndex : IMemoryVectorIndex
{
    private readonly IMongoCollection<MemoryVectorDocument> _vectors;

    public MongoDbMemoryVectorIndex(IMongoDatabase database, IOptions<MongoDbMemoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(database);
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));

        var collectionName = string.IsNullOrWhiteSpace(opt.MemoryVectorsCollection)
            ? "memory_vectors"
            : opt.MemoryVectorsCollection.Trim();

        _vectors = database.GetCollection<MemoryVectorDocument>(collectionName);

        EnsureIndexes();
    }

    public async Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.MemoryId))
            throw new ArgumentException("MemoryVectorRecord.memory_id is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.EntryId))
            throw new ArgumentException("MemoryVectorRecord.entry_id is required.", nameof(record));

        var memoryId = record.MemoryId.Trim();
        var entryId = record.EntryId.Trim();

        var scopeType = (int)(record.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = record.Scope?.ScopeId ?? string.Empty;

        var createdAt = NormalizeCreatedAtUtc(record.CreatedAt);

        var content = (record.Content ?? string.Empty).Trim();
        if (content.Length > 2000)
            content = content[..2000];

        var doc = new MemoryVectorDocument
        {
            Id = $"{memoryId}::{entryId}",
            MemoryId = memoryId,
            EntryId = entryId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            RunId = record.RunId ?? string.Empty,
            AgentId = record.AgentId ?? string.Empty,
            Role = record.Role ?? string.Empty,
            Content = content,
            Tags = record.Tags.ToDictionary(k => k.Key, v => v.Value ?? string.Empty, StringComparer.Ordinal),
            CreatedAt = createdAt,
            Embedding = record.Embedding.ToList()
        };

        var filter = Builders<MemoryVectorDocument>.Filter.Eq(x => x.Id, doc.Id);
        await _vectors.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int limit = 20,
        string? memoryId = null,
        MemoryScopeType? scopeTypeFilter = null,
        string? scopeId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (limit <= 0)
            return Array.Empty<MemoryVectorMatch>();

        memoryId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim();
        scopeId = string.IsNullOrWhiteSpace(scopeId) ? null : scopeId.Trim();

        var filter = Builders<MemoryVectorDocument>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(memoryId))
            filter &= Builders<MemoryVectorDocument>.Filter.Eq(x => x.MemoryId, memoryId);

        if (scopeTypeFilter.HasValue && scopeTypeFilter.Value != MemoryScopeType.Unspecified)
            filter &= Builders<MemoryVectorDocument>.Filter.Eq(x => x.ScopeType, (int)scopeTypeFilter.Value);

        if (!string.IsNullOrWhiteSpace(scopeId))
            filter &= Builders<MemoryVectorDocument>.Filter.Eq(x => x.ScopeId, scopeId);

        // Fetch a bounded candidate set to avoid unbounded memory usage.
        // Heuristic: 50 * limit (capped) - you can tune later.
        var candidateLimit = Math.Min(Math.Max(limit * 50, limit), 5000);

        var docs = await _vectors
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(candidateLimit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var scored = docs
            .Select(d =>
            {
                var sim = CosineSimilarity.Compute(queryEmbedding, d.Embedding);
                return new { Doc = d, Similarity = sim };
            })
            .OrderByDescending(x => x.Similarity)
            .Take(limit)
            .Select(x => new MemoryVectorMatch
            {
                Record = ToMemoryVectorRecord(x.Doc),
                Similarity = x.Similarity
            })
            .ToList();

        return scored;
    }

    private void EnsureIndexes()
    {
        // _id unique exists.
        var idx1 = new CreateIndexModel<MemoryVectorDocument>(
            Builders<MemoryVectorDocument>.IndexKeys
                .Ascending(x => x.MemoryId)
                .Descending(x => x.CreatedAt));
        _vectors.Indexes.CreateOne(idx1);
    }

    private static DateTime NormalizeCreatedAtUtc(Timestamp? createdAt)
    {
        if (createdAt is null || (createdAt.Seconds == 0 && createdAt.Nanos == 0))
            return DateTime.UtcNow;

        var dt = createdAt.ToDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }

    private static MemoryVectorRecord ToMemoryVectorRecord(MemoryVectorDocument doc)
    {
        var r = new MemoryVectorRecord
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
                    r.Tags[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        if (doc.Embedding != null)
        {
            foreach (var v in doc.Embedding)
                r.Embedding.Add(v);
        }

        return r;
    }
}



