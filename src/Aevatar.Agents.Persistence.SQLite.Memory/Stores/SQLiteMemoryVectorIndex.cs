using System.Text.Json;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.SQLite.Internal;
using Aevatar.Agents.Persistence.SQLite.Memory.Internal;
using Aevatar.Agents.Persistence.SQLite.Memory.Options;
using Aevatar.Agents.Persistence.SQLite.Memory.Setup;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Persistence.SQLite.Memory.Stores;

/// <summary>
/// SQLite-backed IMemoryVectorIndex implementation (brute-force cosine similarity).
/// </summary>
public sealed class SQLiteMemoryVectorIndex : IMemoryVectorIndex
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SQLiteConnectionFactory _connectionFactory;
    private readonly string _table;

    public SQLiteMemoryVectorIndex(SQLiteConnectionFactory connectionFactory, IOptions<SQLiteMemoryOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SQLiteMemorySchemaManager.EnsureInitialized(_connectionFactory, opt);
        _table = SQLiteSql.Ident(opt.MemoryVectorsTable, nameof(opt.MemoryVectorsTable));
    }

    public async Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.MemoryId))
            throw new ArgumentException("MemoryVectorRecord.memory_id is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.EntryId))
            throw new ArgumentException("MemoryVectorRecord.entry_id is required.", nameof(record));

        ValidateEmbedding(record.Embedding);

        var memoryId = record.MemoryId.Trim();
        var entryId = record.EntryId.Trim();

        var scopeType = (int)(record.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = record.Scope?.ScopeId ?? string.Empty;

        var runId = record.RunId ?? string.Empty;
        var agentId = record.AgentId ?? string.Empty;
        var role = record.Role ?? string.Empty;

        var createdAtUtc = NormalizeCreatedAtUtc(record.CreatedAt);
        var createdAtSeconds = new DateTimeOffset(createdAtUtc).ToUnixTimeSeconds();

        var content = (record.Content ?? string.Empty).Trim();
        if (content.Length > 2000)
        {
            content = content[..2000];
        }

        var tagsJson = SerializeTags(record.Tags);
        var embeddingJson = SerializeEmbedding(record.Embedding);

        var sql = $@"
INSERT INTO {_table} (memory_id, entry_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at, embedding)
VALUES (@memory_id, @entry_id, @scope_type, @scope_id, @run_id, @agent_id, @role, @content, @tags, @created_at, @embedding)
ON CONFLICT (memory_id, entry_id)
DO UPDATE SET
  scope_type = excluded.scope_type,
  scope_id = excluded.scope_id,
  run_id = excluded.run_id,
  agent_id = excluded.agent_id,
  role = excluded.role,
  content = excluded.content,
  tags = excluded.tags,
  created_at = excluded.created_at,
  embedding = excluded.embedding";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("memory_id", memoryId);
        cmd.Parameters.AddWithValue("entry_id", entryId);
        cmd.Parameters.AddWithValue("scope_type", scopeType);
        cmd.Parameters.AddWithValue("scope_id", scopeId);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("content", content);
        cmd.Parameters.AddWithValue("tags", tagsJson);
        cmd.Parameters.AddWithValue("created_at", createdAtSeconds);
        cmd.Parameters.AddWithValue("embedding", embeddingJson);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

        ValidateEmbedding(queryEmbedding);

        memoryId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim();
        scopeId = string.IsNullOrWhiteSpace(scopeId) ? null : scopeId.Trim();
        var scopeTypeValue = scopeTypeFilter.HasValue ? (int)scopeTypeFilter.Value : (int?)null;

        // 中文: 控制候选集大小，避免内存放大。// ASCII: Bound candidate set size before in-process scoring.
        var candidateLimit = Math.Min(Math.Max(limit * 50, limit), 5000);

        var sql = $@"
SELECT entry_id, memory_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at, embedding
FROM {_table}
WHERE (@memory_id IS NULL OR memory_id = @memory_id)
  AND (@scope_type IS NULL OR scope_type = @scope_type)
  AND (@scope_id IS NULL OR scope_id = @scope_id)
ORDER BY created_at DESC
LIMIT @limit";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("memory_id", (object?)memoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope_type", (object?)scopeTypeValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope_id", (object?)scopeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", candidateLimit);

        var scored = new List<(MemoryVectorRecord Record, double Similarity)>(Math.Min(candidateLimit, 5000));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entryId = reader.GetString(0);
            var rowMemoryId = reader.GetString(1);
            var rowScopeType = (MemoryScopeType)reader.GetInt32(2);
            var rowScopeId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var runId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var agentId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var role = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var content = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            var tagsJson = reader.IsDBNull(8) ? null : reader.GetString(8);
            var createdAtSeconds = reader.IsDBNull(9) ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : reader.GetInt64(9);
            var embeddingJson = reader.IsDBNull(10) ? null : reader.GetString(10);
            var embedding = DeserializeEmbedding(embeddingJson);

            var record = new MemoryVectorRecord
            {
                EntryId = entryId,
                MemoryId = rowMemoryId,
                Scope = new MemoryScope { Type = rowScopeType, ScopeId = rowScopeId },
                RunId = runId,
                AgentId = agentId,
                Role = role,
                Content = content,
                CreatedAt = Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(createdAtSeconds).UtcDateTime)
            };

            foreach (var kv in DeserializeTags(tagsJson))
            {
                record.Tags[kv.Key] = kv.Value;
            }

            foreach (var v in embedding)
            {
                record.Embedding.Add(v);
            }

            var similarity = CosineSimilarity.Compute(queryEmbedding, embedding);
            scored.Add((record, similarity));
        }

        return scored
            .OrderByDescending(x => x.Similarity)
            .Take(limit)
            .Select(x => new MemoryVectorMatch
            {
                Record = x.Record,
                Similarity = x.Similarity
            })
            .ToList();
    }

    private static void ValidateEmbedding(IReadOnlyList<float> embedding)
    {
        if (embedding.Count == 0)
        {
            throw new ArgumentException("Embedding cannot be empty.", nameof(embedding));
        }

        foreach (var v in embedding)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new ArgumentException("Embedding contains NaN/Infinity, which cannot be stored.");
            }
        }
    }

    private static DateTime NormalizeCreatedAtUtc(Timestamp? createdAt)
    {
        if (createdAt is null || (createdAt.Seconds == 0 && createdAt.Nanos == 0))
        {
            return DateTime.UtcNow;
        }

        var dt = createdAt.ToDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }

    private static string SerializeTags(Google.Protobuf.Collections.MapField<string, string> tags)
    {
        if (tags.Count == 0)
        {
            return "{}";
        }

        var dict = new Dictionary<string, string>(tags.Count, StringComparer.Ordinal);
        foreach (var kv in tags)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key))
            {
                dict[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        return JsonSerializer.Serialize(dict, DefaultJsonOptions);
    }

    private static IReadOnlyDictionary<string, string> DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(0);
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, DefaultJsonOptions);
            return dict ?? new Dictionary<string, string>(0);
        }
        catch
        {
            return new Dictionary<string, string>(0);
        }
    }

    private static string SerializeEmbedding(IReadOnlyList<float> embedding)
    {
        return JsonSerializer.Serialize(embedding, DefaultJsonOptions);
    }

    private static IReadOnlyList<float> DeserializeEmbedding(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<float>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<float>>(json, DefaultJsonOptions)
                ?? new List<float>();
        }
        catch
        {
            return Array.Empty<float>();
        }
    }
}
