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
//  SupabaseMemoryStore
//
//  IMemoryStore 的 Supabase(Postgres) 实现：
//  - MemoryEntry 以 text/jsonb/timestamptz 形式存储
//  - 主键 (memory_id, entry_id) => append 语义 + 幂等写入
//  - SearchAsync: 可选 FTS（GIN），否则退化 ILIKE
// ============================================================
public sealed class SupabaseMemoryStore : IMemoryStore
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabaseMemoryOptions _options;
    private readonly string _table;

    public SupabaseMemoryStore(NpgsqlDataSource dataSource, IOptions<SupabaseMemoryOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SupabaseMemorySchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.MemoryEntriesTable, nameof(_options.MemoryEntriesTable));
    }

    public async Task AppendAsync(MemoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(entry.MemoryId))
        {
            throw new ArgumentException("MemoryEntry.memory_id is required.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.EntryId))
        {
            throw new ArgumentException("MemoryEntry.entry_id is required.", nameof(entry));
        }

        var memoryId = entry.MemoryId.Trim();
        var entryId = entry.EntryId.Trim();

        var scopeType = (int)(entry.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = entry.Scope?.ScopeId ?? string.Empty;

        var runId = entry.RunId ?? string.Empty;
        var agentId = entry.AgentId ?? string.Empty;
        var role = entry.Role ?? string.Empty;
        var content = entry.Content ?? string.Empty;

        var createdAtUtc = NormalizeCreatedAtUtc(entry.CreatedAt);

        var tagsJson = SerializeTags(entry.Tags);

        var sql = $@"
INSERT INTO {_table} (memory_id, entry_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at)
VALUES (@memory_id, @entry_id, @scope_type, @scope_id, @run_id, @agent_id, @role, @content, @tags, @created_at)
ON CONFLICT (memory_id, entry_id)
DO NOTHING";

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

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryResourceSummary>> ListResourcesAsync(
        MemoryScopeType? scopeTypeFilter = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return Array.Empty<MemoryResourceSummary>();
        }

        var sql = $@"
SELECT memory_id, scope_type, scope_id, COUNT(*)::int AS entry_count, MAX(created_at) AS latest_at
FROM {_table}
WHERE (@scope_type IS NULL OR scope_type = @scope_type)
GROUP BY memory_id, scope_type, scope_id
ORDER BY latest_at DESC
LIMIT @limit";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var scopeTypeValue = scopeTypeFilter.HasValue ? (int)scopeTypeFilter.Value : (int?)null;
        cmd.Parameters.AddWithValue("scope_type", (object?)scopeTypeValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<MemoryResourceSummary>(Math.Min(limit, 200));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var memoryId = reader.GetString(0);
            var scopeType = (MemoryScopeType)reader.GetInt32(1);
            var scopeId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var entryCount = reader.GetInt32(3);
            var latestAt = reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4);

            list.Add(new MemoryResourceSummary
            {
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = scopeType, ScopeId = scopeId },
                EntryCount = entryCount,
                LatestAt = Timestamp.FromDateTime(latestAt.ToUniversalTime())
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListEntriesAsync(
        string memoryId,
        int limit = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            throw new ArgumentException("memoryId cannot be null/empty.", nameof(memoryId));
        }

        ct.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return Array.Empty<MemoryEntry>();
        }

        memoryId = memoryId.Trim();

        var sql = $@"
SELECT entry_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at
FROM {_table}
WHERE memory_id = @memory_id
ORDER BY created_at DESC
LIMIT @limit";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("memory_id", memoryId);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<MemoryEntry>(Math.Min(limit, 200));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entryId = reader.GetString(0);
            var scopeType = (MemoryScopeType)reader.GetInt32(1);
            var scopeId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var runId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var agentId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var role = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var content = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var tagsJson = ReadJson(reader, 7);
            var createdAt = reader.IsDBNull(8) ? DateTime.UtcNow : reader.GetDateTime(8);

            var entry = new MemoryEntry
            {
                EntryId = entryId,
                MemoryId = memoryId,
                Scope = new MemoryScope { Type = scopeType, ScopeId = scopeId },
                RunId = runId,
                AgentId = agentId,
                Role = role,
                Content = content,
                CreatedAt = Timestamp.FromDateTime(createdAt.ToUniversalTime())
            };

            foreach (var kv in DeserializeTags(tagsJson))
            {
                entry.Tags[kv.Key] = kv.Value;
            }

            list.Add(entry);
        }

        return list;
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 50,
        MemoryScopeType? scopeTypeFilter = null,
        string? memoryId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MemoryEntry>();
        }

        ct.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return Array.Empty<MemoryEntry>();
        }

        query = query.Trim();
        memoryId = string.IsNullOrWhiteSpace(memoryId) ? null : memoryId.Trim();

        var scopeTypeValue = scopeTypeFilter.HasValue ? (int)scopeTypeFilter.Value : (int?)null;

        if (_options.EnableFullTextSearch)
        {
            var cfg = SupabaseSql.RegConfigLiteral(_options.FtsRegConfig);
            var sql = $@"
SELECT entry_id, memory_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at,
       ts_rank_cd(to_tsvector({cfg}, content), websearch_to_tsquery({cfg}, @q)) AS rank
FROM {_table}
WHERE (@memory_id IS NULL OR memory_id = @memory_id)
  AND (@scope_type IS NULL OR scope_type = @scope_type)
  AND to_tsvector({cfg}, content) @@ websearch_to_tsquery({cfg}, @q)
ORDER BY rank DESC, created_at DESC
LIMIT @limit";

            return await ExecuteSearchAsync(sql, query, limit, memoryId, scopeTypeValue, ct).ConfigureAwait(false);
        }
        else
        {
            var sql = $@"
SELECT entry_id, memory_id, scope_type, scope_id, run_id, agent_id, role, content, tags, created_at
FROM {_table}
WHERE (@memory_id IS NULL OR memory_id = @memory_id)
  AND (@scope_type IS NULL OR scope_type = @scope_type)
  AND content ILIKE ('%' || @q || '%')
ORDER BY created_at DESC
LIMIT @limit";

            return await ExecuteSearchAsync(sql, query, limit, memoryId, scopeTypeValue, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<MemoryEntry>> ExecuteSearchAsync(
        string sql,
        string query,
        int limit,
        string? memoryId,
        int? scopeType,
        CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("q", query);
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("memory_id", (object?)memoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scope_type", (object?)scopeType ?? DBNull.Value);

        var list = new List<MemoryEntry>(Math.Min(limit, 200));

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var entryId = reader.GetString(0);
            var rowMemoryId = reader.GetString(1);
            var scopeTypeEnum = (MemoryScopeType)reader.GetInt32(2);
            var scopeId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var runId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var agentId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var role = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var content = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            var tagsJson = ReadJson(reader, 8);
            var createdAt = reader.IsDBNull(9) ? DateTime.UtcNow : reader.GetDateTime(9);

            var entry = new MemoryEntry
            {
                EntryId = entryId,
                MemoryId = rowMemoryId,
                Scope = new MemoryScope { Type = scopeTypeEnum, ScopeId = scopeId },
                RunId = runId,
                AgentId = agentId,
                Role = role,
                Content = content,
                CreatedAt = Timestamp.FromDateTime(createdAt.ToUniversalTime())
            };

            foreach (var kv in DeserializeTags(tagsJson))
            {
                entry.Tags[kv.Key] = kv.Value;
            }

            list.Add(entry);
        }

        return list;
    }

    private static DateTime NormalizeCreatedAtUtc(Timestamp? createdAt)
    {
        if (createdAt is null || createdAt.Seconds == 0 && createdAt.Nanos == 0)
        {
            return DateTime.UtcNow;
        }

        // Timestamp is always UTC; ensure we persist UTC to timestamptz.
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


