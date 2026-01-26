using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Persistence.SQLite.GAgent.Internal;
using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Aevatar.Agents.Persistence.SQLite.GAgent.Setup;
using Aevatar.Agents.Persistence.SQLite.Internal;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Stores;

/// <summary>
/// SQLite configuration store implementation.
/// </summary>
public sealed class SQLiteConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    private readonly SQLiteConnectionFactory _connectionFactory;
    private readonly string _table;
    private readonly string _configTypeName;

    public SQLiteConfigStore(SQLiteConnectionFactory connectionFactory, IOptions<SQLitePersistenceOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SQLiteSchemaManager.EnsureInitialized(_connectionFactory, opt);

        _table = SQLiteSql.Ident(opt.AgentConfigsTable, nameof(opt.AgentConfigsTable));
        _configTypeName = typeof(TConfig).FullName ?? typeof(TConfig).Name;
    }

    public async Task<TConfig?> LoadAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var agentTypeName = agentType.FullName ?? agentType.Name;

        var sql = $"SELECT config_data FROM {_table} WHERE config_type = @config_type AND agent_type = @agent_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("config_type", _configTypeName);
        cmd.Parameters.AddWithValue("agent_type", agentTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return null;
        }

        var json = result.ToString() ?? string.Empty;
        return SQLiteConfigJson.Deserialize<TConfig>(json);
    }

    public async Task SaveAsync(Type agentType, string agentId, TConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var agentTypeName = agentType.FullName ?? agentType.Name;
        var json = SQLiteConfigJson.Serialize(config);
        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sql = $@"
INSERT INTO {_table} (config_type, agent_type, agent_id, config_data, updated_at)
VALUES (@config_type, @agent_type, @agent_id, @config_data, @updated_at)
ON CONFLICT (config_type, agent_type, agent_id)
DO UPDATE SET
  config_data = excluded.config_data,
  updated_at = excluded.updated_at";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("config_type", _configTypeName);
        cmd.Parameters.AddWithValue("agent_type", agentTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("config_data", json);
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var agentTypeName = agentType.FullName ?? agentType.Name;

        var sql = $"DELETE FROM {_table} WHERE config_type = @config_type AND agent_type = @agent_type AND agent_id = @agent_id";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("config_type", _configTypeName);
        cmd.Parameters.AddWithValue("agent_type", agentTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(Type agentType, string agentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentType);
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();
        var agentTypeName = agentType.FullName ?? agentType.Name;

        var sql = $"SELECT 1 FROM {_table} WHERE config_type = @config_type AND agent_type = @agent_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("config_type", _configTypeName);
        cmd.Parameters.AddWithValue("agent_type", agentTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }
}

/// <summary>
/// SQLite ConfigStore factory (for manual DI/advanced scenarios).
/// </summary>
public static class SQLiteConfigurationStoreFactory
{
    public static Func<IServiceProvider, object> Create<TConfig>()
        where TConfig : class, new()
    {
        return sp =>
        {
            var factory = sp.GetService(typeof(SQLiteConnectionFactory)) as SQLiteConnectionFactory
                ?? throw new InvalidOperationException(
                    "SQLiteConnectionFactory not registered. Call services.AddAevatarSQLite(...) first.");

            var options = sp.GetService(typeof(IOptions<SQLitePersistenceOptions>)) as IOptions<SQLitePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SQLitePersistenceOptions not registered. Call services.AddAevatarSQLiteGAgent(...) first.");

            return new SQLiteConfigStore<TConfig>(factory, options);
        };
    }
}
