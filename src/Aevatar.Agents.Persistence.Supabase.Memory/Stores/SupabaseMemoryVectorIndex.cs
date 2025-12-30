using System.Globalization;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;
using Aevatar.Agents.Persistence.Supabase.Memory.Setup;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Aevatar.Agents.Persistence.Supabase.Memory.Stores;

// ============================================================
//  SupabaseMemoryVectorIndex
//
//  IMemoryVectorIndex 的 Supabase(Postgres + pgvector) 实现：
//  - embedding 存 pgvector: vector(dims)
//  - SearchAsync 使用 pgvector operator 做 top-k
//
//  NOTE:
//  - 这里为了避免引入额外依赖包，使用 \"[... ]\" 字符串 + ::vector 的方式传参。
// ============================================================
public sealed class SupabaseMemoryVectorIndex : IMemoryVectorIndex
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabaseMemoryOptions _options;
    private readonly string _table;

    public SupabaseMemoryVectorIndex(NpgsqlDataSource dataSource, IOptions<SupabaseMemoryOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SupabaseMemorySchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.MemoryVectorsTable, nameof(_options.MemoryVectorsTable));
    }

    public async Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(record.MemoryId))
        {
            throw new ArgumentException("MemoryVectorRecord.memory_id is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.EntryId))
        {
            throw new ArgumentException("MemoryVectorRecord.entry_id is required.", nameof(record));
        }

        ValidateEmbedding(record.Embedding, _options.VectorDimensions);

        var memoryId = record.MemoryId.Trim();
        var entryId = record.EntryId.Trim();

        var scopeType = (int)(record.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = record.Scope?.ScopeId ?? string.Empty;

        var runId = record.RunId ?? string.Empty;
        var agentId = record.AgentId ?? string.Empty;
        var role = record.Role ?? string.Empty;

        var createdAtUtc = NormalizeCreatedAtUtc(record.CreatedAt);

        // Store a compact snippet (NOT canonical source of truth).
        var content = (record.Content ?? string.Empty).Trim();
        if (content.Length > 2000)
        {
            content = content[..2000];
        }

        var tagsJson = SerializeTags(record.Tags);
        var embeddingLiteral = ToVectorLiteral(record.Embedding);

        var sql = $@"
INSERT INTO {_table} (memory_id, entry_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at, embedding)
VALUES (@memory_id, @entry_id, @scope_type, @scope_id, @run_id, @agent_id, @role, @content, @tags, @created_at, @embedding::vector)
ON CONFLICT (memory_id, entry_id)
DO UPDATE SET
  scope_type = EXCLUDED.scope_type,
  scope_id = EXCLUDED.scope_id,
  run_id = EXCLUDED.run_id,
  agent_id = EXCLUDED.agent_id,
  role = EXCLUDED.role,
  content = EXCLUDED.content,
  tags = EXCLUDED.tags,
  created_at = EXCLUDED.created_at,
  embedding = EXCLUDED.embedding";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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
        var p = cmd.Parameters.AddWithValue("tags", NpgsqlDbType.Jsonb, tagsJson);
        p.Value = tagsJson;
        cmd.Parameters.AddWithValue("created_at", createdAtUtc);
        cmd.Parameters.AddWithValue("embedding", embeddingLiteral);

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
        ct.ThrowIfCancellationRequested();

        if (limit <= 0)
        {
            return Array.Empty<MemoryVectorMatch>();
        }

        ValidateEmbedding(queryEmbedding, _options.VectorDimensions);

        memoryId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim();
        scopeId = string.IsNullOrWhiteSpace(scopeId) ? null : scopeId.Trim();
        var scopeTypeValue = scopeTypeFilter.HasValue ? (int)scopeTypeFilter.Value : (int?)null;

        var queryLiteral = ToVectorLiteral(queryEmbedding);

        var (distanceExpr, similarityExpr) = GetDistanceAndSimilarityExpr(_options.DistanceMetric);

        var sql = $@"
SELECT entry_id, memory_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at,
       {similarityExpr} AS similarity
FROM {_table}
WHERE (@memory_id IS NULL OR memory_id = @memory_id)
  AND (@scope_type IS NULL OR scope_type = @scope_type)
  AND (@scope_id IS NULL OR scope_id = @scope_id)
ORDER BY {distanceExpr} ASC
LIMIT @limit";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("memory_id", (object?)memoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope_type", (object?)scopeTypeValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope_id", (object?)scopeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("q", queryLiteral);

        var list = new List<MemoryVectorMatch>(Math.Min(limit, 200));

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
            var tagsJson = ReadJson(reader, 8);
            var createdAt = reader.IsDBNull(9) ? DateTime.UtcNow : reader.GetDateTime(9);
            var similarity = reader.IsDBNull(10) ? 0d : Convert.ToDouble(reader.GetValue(10), CultureInfo.InvariantCulture);

            var record = new MemoryVectorRecord
            {
                EntryId = entryId,
                MemoryId = rowMemoryId,
                Scope = new MemoryScope { Type = rowScopeType, ScopeId = rowScopeId },
                RunId = runId,
                AgentId = agentId,
                Role = role,
                Content = content,
                CreatedAt = Timestamp.FromDateTime(createdAt.ToUniversalTime())
            };

            foreach (var kv in DeserializeTags(tagsJson))
            {
                record.Tags[kv.Key] = kv.Value;
            }

            list.Add(new MemoryVectorMatch
            {
                Record = record,
                Similarity = similarity
            });
        }

        return list;
    }

    private static (string DistanceExpr, string SimilarityExpr) GetDistanceAndSimilarityExpr(SupabaseVectorDistanceMetric metric)
    {
        // We pass query embedding as @q::vector.
        return metric switch
        {
            SupabaseVectorDistanceMetric.L2 => (
                DistanceExpr: "embedding <-> @q::vector",
                SimilarityExpr: "1.0 / (1.0 + (embedding <-> @q::vector))"),

            SupabaseVectorDistanceMetric.InnerProduct => (
                DistanceExpr: "embedding <#> @q::vector",
                SimilarityExpr: "-(embedding <#> @q::vector)"),

            _ => (
                DistanceExpr: "embedding <=> @q::vector",
                SimilarityExpr: "1.0 - (embedding <=> @q::vector)")
        };
    }

    private static void ValidateEmbedding(IReadOnlyCollection<float> embedding, int expectedDimensions)
    {
        if (expectedDimensions <= 0)
        {
            throw new InvalidOperationException("VectorDimensions must be configured (>0).");
        }

        if (embedding.Count != expectedDimensions)
        {
            throw new ArgumentException(
                $"Embedding dimensions mismatch. Expected {expectedDimensions}, got {embedding.Count}.");
        }
    }

    private static string ToVectorLiteral(IReadOnlyList<float> embedding)
    {
        // Format: [0.1,0.2,...]
        // Use invariant culture to avoid commas as decimal separators.
        var sb = new StringBuilder(embedding.Count * 8);
        sb.Append('[');
        for (var i = 0; i < embedding.Count; i++)
        {
            if (i > 0) sb.Append(',');

            var v = embedding[i];
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new ArgumentException("Embedding contains NaN/Infinity, which cannot be stored in pgvector.");
            }

            sb.Append(v.ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static DateTime NormalizeCreatedAtUtc(Timestamp? createdAt)
    {
        if (createdAt is null || createdAt.Seconds == 0 && createdAt.Nanos == 0)
        {
            return DateTime.UtcNow;
        }

        var dt = createdAt.ToDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
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

    private static string? ReadJson(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var result = reader.GetValue(ordinal);
        return result switch
        {
            string s => s,
            JsonDocument doc => doc.RootElement.GetRawText(),
            JsonElement el => el.GetRawText(),
            _ => result.ToString()
        };
    }
}


