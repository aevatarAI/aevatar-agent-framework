using System.Text.Json;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.Supabase.GAgent.Internal;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Setup;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.Stores;

/// <summary>
/// Supabase(Postgres) ConfigStore implementation:
/// - Stores configuration objects as jsonb
/// - Uses (config_type, agent_type, agent_id) as primary key to achieve isolation and idempotent upsert
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public sealed class SupabaseConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabasePersistenceOptions _options;
    private readonly string _table;
    private readonly string _configTypeName;

    public SupabaseConfigStore(NpgsqlDataSource dataSource, IOptions<SupabasePersistenceOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SupabaseSchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.AgentConfigsTable, nameof(_options.AgentConfigsTable));
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

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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

        // Common Npgsql jsonb return types: string / JsonDocument / JsonElement.
        var json = result switch
        {
            string s => s,
            JsonDocument doc => doc.RootElement.GetRawText(),
            JsonElement el => el.GetRawText(),
            _ => result.ToString() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return SupabaseConfigJson.Deserialize<TConfig>(json);
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
        var json = SupabaseConfigJson.Serialize(config);

        var sql = $@"
INSERT INTO {_table} (config_type, agent_type, agent_id, config_data, updated_at)
VALUES (@config_type, @agent_type, @agent_id, @config_data, now())
ON CONFLICT (config_type, agent_type, agent_id)
DO UPDATE SET
  config_data = EXCLUDED.config_data,
  updated_at = EXCLUDED.updated_at";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.AddWithValue("config_type", _configTypeName);
        cmd.Parameters.AddWithValue("agent_type", agentTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var p = cmd.Parameters.AddWithValue("config_data", NpgsqlDbType.Jsonb, json);
        p.Value = json;

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

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
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
/// Supabase ConfigStore factory (for manual DI/advanced scenarios).
/// </summary>
public static class SupabaseConfigurationStoreFactory
{
    public static Func<IServiceProvider, object> Create<TConfig>()
        where TConfig : class, new()
    {
        return sp =>
        {
            var dataSource = sp.GetService(typeof(NpgsqlDataSource)) as NpgsqlDataSource
                ?? throw new InvalidOperationException(
                    "NpgsqlDataSource not registered. Call services.AddAevatarSupabase(...) first.");

            var options = sp.GetService(typeof(IOptions<SupabasePersistenceOptions>)) as IOptions<SupabasePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SupabasePersistenceOptions not registered. Call services.AddAevatarSupabaseGAgent(...) first.");

            return new SupabaseConfigStore<TConfig>(dataSource, options);
        };
    }
}


