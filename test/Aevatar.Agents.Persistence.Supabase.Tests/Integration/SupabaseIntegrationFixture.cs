using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Setup;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests.Integration;

/// <summary>
/// Optional integration fixture.
///
/// To enable, set env var:
/// - AEVATAR_SUPABASE_TEST_CONNECTION_STRING
///
/// If not set, tests will be skipped (see test methods).
/// </summary>
public sealed class SupabaseIntegrationFixture : IAsyncLifetime
{
    private const string ConnectionStringEnv = "AEVATAR_SUPABASE_TEST_CONNECTION_STRING";

    public string? ConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringEnv);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

    public string Schema { get; } =
        $"aevatar_test_{Guid.NewGuid():N}".ToLowerInvariant();

    public NpgsqlDataSource? DataSource { get; private set; }

    public IOptions<SupabasePersistenceOptions>? Options { get; private set; }

    public Task InitializeAsync()
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        var connectionString = ConnectionString!.Trim();

        var options = new SupabasePersistenceOptions
        {
            ConnectionString = connectionString,
            Schema = Schema,

            // 测试环境尽量不动权限/RLS，避免连接串权限不够导致初始化失败
            LockDownPublicAccess = false,
            EnableRowLevelSecurity = false
        };

        DataSource = NpgsqlDataSource.Create(connectionString);
        Options = Microsoft.Extensions.Options.Options.Create(options);

        // Use the same initialization path as production code (and populate its in-process cache).
        SupabaseSchemaManager.EnsureInitialized(DataSource, options);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            if (DataSource != null)
            {
                var schema = SupabaseSql.Ident(Schema, nameof(Schema));

                await using var conn = await DataSource.OpenConnectionAsync().ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (DataSource != null)
            {
                await DataSource.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}


