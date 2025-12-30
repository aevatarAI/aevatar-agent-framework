using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Setup;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.Stores;

/// <summary>
/// Supabase(Postgres) StateStore implementation:
/// - Uses Protobuf bytea storage
/// - Implements idempotent upsert via (state_type, agent_id) unique key
/// - version field used for EventSourcing snapshot version marking (current framework semantics)
/// </summary>
/// <typeparam name="TState">Must be Protobuf IMessage</typeparam>
public sealed class SupabaseStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabasePersistenceOptions _options;
    private readonly string _table;
    private readonly string _stateTypeName;

    public SupabaseStateStore(NpgsqlDataSource dataSource, IOptions<SupabasePersistenceOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Auto-initialize (idempotent)
        SupabaseSchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.AgentStatesTable, nameof(_options.AgentStatesTable));
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;
    }

    public async Task<TState?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        const string selectColumns = "state_data";
        var sql = $"SELECT {selectColumns} FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var data = reader.IsDBNull(0) ? null : reader.GetFieldValue<byte[]>(0);
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
        // NOTE:
        // - Parameter name in Abstractions is expectedVersion, but current framework semantics is \"snapshot version\".
        => SaveInternalAsync(agentId, state, expectedVersion, ct);

    public async Task<long> GetCurrentVersionAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT version FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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

        var sql = $@"
INSERT INTO {_table} (state_type, agent_id, state_data, version, updated_at)
VALUES (@state_type, @agent_id, @state_data, @version, now())
ON CONFLICT (state_type, agent_id)
DO UPDATE SET
  state_data = EXCLUDED.state_data,
  version = EXCLUDED.version,
  updated_at = EXCLUDED.updated_at";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("state_data", data);
        cmd.Parameters.AddWithValue("version", version);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Supabase StateStore factory (for manual DI/advanced scenarios).
/// </summary>
public static class SupabaseStateStoreFactory
{
    public static Func<IServiceProvider, object> Create<TState>()
        where TState : class, IMessage<TState>, new()
    {
        return sp =>
        {
            var dataSource = sp.GetService(typeof(NpgsqlDataSource)) as NpgsqlDataSource
                ?? throw new InvalidOperationException(
                    "NpgsqlDataSource not registered. Call services.AddAevatarSupabase(...) first.");

            var options = sp.GetService(typeof(IOptions<SupabasePersistenceOptions>)) as IOptions<SupabasePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SupabasePersistenceOptions not registered. Call services.AddAevatarSupabaseGAgent(...) first.");

            return new SupabaseStateStore<TState>(dataSource, options);
        };
    }
}


