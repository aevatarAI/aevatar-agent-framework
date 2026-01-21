using System.Collections.Concurrent;
using Aevatar.Agents.Persistence.SQLite;
using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Microsoft.Data.Sqlite;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Setup;

/// <summary>
/// Runtime auto-initialization (create tables/indexes) for SQLite.
/// </summary>
internal static class SQLiteSchemaManager
{
    private static readonly ConcurrentDictionary<string, bool> Initialized = new();

    internal static void EnsureInitialized(SQLiteConnectionFactory factory, SQLitePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.AutoCreateTables && !options.AutoCreateIndexes)
        {
            return;
        }

        // 中文: 同一连接串+配置只初始化一次，避免重复 DDL。// ASCII: Idempotent initialization per database/options.
        var key = BuildKey(factory, options);
        if (!Initialized.TryAdd(key, true))
        {
            return;
        }

        try
        {
            using var conn = factory.CreateConnection();
            conn.Open();
            ExecuteStatements(conn, SQLiteSchemaScript.BuildStatements(options));
        }
        catch
        {
            Initialized.TryRemove(key, out _);
            throw;
        }
    }

    private static void ExecuteStatements(SqliteConnection conn, IReadOnlyList<string> statements)
    {
        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static string BuildKey(SQLiteConnectionFactory factory, SQLitePersistenceOptions options)
    {
        return string.Join(
            "|",
            factory.ConnectionString,
            options.AgentStatesTable,
            options.AgentConfigsTable,
            options.EventRouterHierarchiesTable,
            options.AutoCreateTables.ToString(),
            options.AutoCreateIndexes.ToString());
    }
}
