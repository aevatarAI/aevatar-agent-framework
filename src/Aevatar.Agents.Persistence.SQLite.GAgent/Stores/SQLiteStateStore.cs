using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Aevatar.Agents.Persistence.SQLite.GAgent.Setup;
using Aevatar.Agents.Persistence.SQLite.Internal;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Stores;

/// <summary>
/// SQLite state store implementation with Protobuf serialization.
/// </summary>
public sealed class SQLiteStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly SQLiteConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _stateTypeName;

    public SQLiteStateStore(SQLiteConnectionFactory connectionFactory, IOptions<SQLitePersistenceOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SQLiteSchemaManager.EnsureInitialized(_connectionFactory, opt);

        _table = SQLiteSql.Ident(opt.AgentStatesTable, nameof(opt.AgentStatesTable));
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;
    }

    public async Task<TState?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT state_data FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var data = reader.IsDBNull(0) ? null : (byte[])reader["state_data"];
        if (data == null || data.Length == 0)
        {
            return null;
        }

        var state = new TState();
        state.MergeFrom(data);
        return state;
    }

    public Task SaveAsync(string agentId, TState state, CancellationToken ct = default)
        => SaveInternalAsync(agentId, state, version: 1, ct);

    public Task SaveAsync(string agentId, TState state, long expectedVersion, CancellationToken ct = default)
        // 当前框架语义：expectedVersion 表示快照版本。// Snapshot version semantics.
        => SaveInternalAsync(agentId, state, expectedVersion, ct);

    public async Task<long> GetCurrentVersionAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT version FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return 0;
        }

        return Convert.ToInt64(result);
    }

    public async Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var sql = $"DELETE FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
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
        var sql = $"SELECT 1 FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private async Task SaveInternalAsync(string agentId, TState state, long version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var data = state.ToByteArray();
        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sql = $@"
INSERT INTO {_table} (state_type, agent_id, state_data, version, updated_at)
VALUES (@state_type, @agent_id, @state_data, @version, @updated_at)
ON CONFLICT (state_type, agent_id)
DO UPDATE SET
  state_data = excluded.state_data,
  version = excluded.version,
  updated_at = excluded.updated_at";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("state_data", data);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// SQLite StateStore factory (for manual DI/advanced scenarios).
/// </summary>
public static class SQLiteStateStoreFactory
{
    public static Func<IServiceProvider, object> Create<TState>()
        where TState : class, IMessage<TState>, new()
    {
        return sp =>
        {
            var factory = sp.GetService(typeof(SQLiteConnectionFactory)) as SQLiteConnectionFactory
                ?? throw new InvalidOperationException(
                    "SQLiteConnectionFactory not registered. Call services.AddAevatarSQLite(...) first.");

            var options = sp.GetService(typeof(IOptions<SQLitePersistenceOptions>)) as IOptions<SQLitePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SQLitePersistenceOptions not registered. Call services.AddAevatarSQLiteGAgent(...) first.");

            return new SQLiteStateStore<TState>(factory, options);
        };
    }
}
