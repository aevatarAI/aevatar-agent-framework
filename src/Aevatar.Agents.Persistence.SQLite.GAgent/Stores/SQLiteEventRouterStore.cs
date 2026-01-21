using System.Text.Json;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Aevatar.Agents.Persistence.SQLite.GAgent.Setup;
using Aevatar.Agents.Persistence.SQLite.Internal;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Stores;

/// <summary>
/// SQLite EventRouter hierarchy store implementation.
/// </summary>
public sealed class SQLiteEventRouterStore : IEventRouterStore
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SQLiteConnectionFactory _connectionFactory;
    private readonly string _table;

    public SQLiteEventRouterStore(SQLiteConnectionFactory connectionFactory, IOptions<SQLitePersistenceOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SQLiteSchemaManager.EnsureInitialized(_connectionFactory, opt);

        _table = SQLiteSql.Ident(opt.EventRouterHierarchiesTable, nameof(opt.EventRouterHierarchiesTable));
    }

    public async Task<EventRouterHierarchy?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT parent_id, children_ids FROM {_table} WHERE agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var parentId = reader.IsDBNull(0) ? null : reader.GetString(0);
        var childrenJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        var childrenIds = DeserializeChildren(childrenJson);

        return new EventRouterHierarchy
        {
            ParentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim(),
            ChildrenIds = new HashSet<string>(childrenIds, StringComparer.Ordinal)
        };
    }

    public async Task SaveAsync(string agentId, EventRouterHierarchy hierarchy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var parentId = string.IsNullOrWhiteSpace(hierarchy.ParentId) ? null : hierarchy.ParentId.Trim();
        var childrenJson = SerializeChildren(hierarchy.ChildrenIds);
        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sql = $@"
INSERT INTO {_table} (agent_id, parent_id, children_ids, updated_at)
VALUES (@agent_id, @parent_id, @children_ids, @updated_at)
ON CONFLICT (agent_id)
DO UPDATE SET
  parent_id = excluded.parent_id,
  children_ids = excluded.children_ids,
  updated_at = excluded.updated_at";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("parent_id", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("children_ids", childrenJson);
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var sql = $"DELETE FROM {_table} WHERE agent_id = @agent_id";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var sql = $"SELECT 1 FROM {_table} WHERE agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static string SerializeChildren(IEnumerable<string>? childrenIds)
    {
        if (childrenIds == null)
        {
            return "[]";
        }

        var list = childrenIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return JsonSerializer.Serialize(list, DefaultJsonOptions);
    }

    private static IReadOnlyList<string> DeserializeChildren(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, DefaultJsonOptions)
                ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// SQLite EventRouter store factory (for manual DI/advanced scenarios).
/// </summary>
public static class SQLiteEventRouterStoreFactory
{
    public static Func<IServiceProvider, IEventRouterStore> Create()
    {
        return sp =>
        {
            var factory = sp.GetService(typeof(SQLiteConnectionFactory)) as SQLiteConnectionFactory
                ?? throw new InvalidOperationException(
                    "SQLiteConnectionFactory not registered. Call services.AddAevatarSQLite(...) first.");

            var options = sp.GetService(typeof(IOptions<SQLitePersistenceOptions>)) as IOptions<SQLitePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SQLitePersistenceOptions not registered. Call services.AddAevatarSQLiteGAgent(...) first.");

            return new SQLiteEventRouterStore(factory, options);
        };
    }
}
