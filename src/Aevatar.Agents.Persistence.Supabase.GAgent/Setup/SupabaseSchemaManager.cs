using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.Setup;

/// <summary>
/// Runtime auto-initialization (create tables/indexes/tighten permissions/RLS).
///
/// Constraints:
/// - Database user must have corresponding DDL permissions; otherwise will throw exception.
/// - Initialization is idempotent (IF NOT EXISTS + DO block), and executes only once per DataSource/Schema.
/// </summary>
internal static class SupabaseSchemaManager
{
    private static readonly ConcurrentDictionary<string, bool> Initialized = new();

    internal static void EnsureInitialized(NpgsqlDataSource dataSource, SupabasePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        // If all switches are off, skip directly.
        if (!options.AutoCreateSchema &&
            !options.AutoCreateTables &&
            !options.AutoCreateIndexes &&
            !options.LockDownPublicAccess &&
            !options.EnableRowLevelSecurity)
        {
            return;
        }

        var key = BuildKey(dataSource, options);
        if (!Initialized.TryAdd(key, true))
        {
            return;
        }

        try
        {
            using var conn = dataSource.OpenConnection();
            ExecuteStatements(conn, SupabaseSchemaScript.BuildStatements(options));
        }
        catch
        {
            // Initialization failure allows retry (avoid locking process forever after one failure).
            Initialized.TryRemove(key, out _);
            throw;
        }
    }

    private static void ExecuteStatements(NpgsqlConnection conn, IReadOnlyList<string> statements)
    {
        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static string BuildKey(NpgsqlDataSource dataSource, SupabasePersistenceOptions options)
    {
        // Construct key using DataSource's identity hash + schema/table configuration.
        // Purpose: avoid duplicate DDL within same process, and allow multiple data sources to coexist.
        var dsKey = RuntimeHelpers.GetHashCode(dataSource);

        return string.Join(
            "|",
            dsKey.ToString(),
            options.Schema,
            options.AgentStatesTable,
            options.AgentConfigsTable,
            options.EventRouterHierarchiesTable,
            options.AutoCreateSchema.ToString(),
            options.AutoCreateTables.ToString(),
            options.AutoCreateIndexes.ToString(),
            options.LockDownPublicAccess.ToString(),
            options.EnableRowLevelSecurity.ToString(),
            options.ForceRowLevelSecurity.ToString(),
            options.CreateServiceRolePolicies.ToString());
    }
}


