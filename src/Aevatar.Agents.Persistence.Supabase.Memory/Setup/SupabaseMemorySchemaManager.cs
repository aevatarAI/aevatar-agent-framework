using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Memory.Setup;

// ============================================================
//  SupabaseMemorySchemaManager
//
//  目的 / Purpose:
//  - 在进程内对 schema/table/index 做一次性（per DataSource + options）幂等初始化。
//
//  原则 / Principles:
//  - IF NOT EXISTS + DO block => 可重复执行
//  - 失败允许重试（避免一次失败把进程永久锁死在“已初始化”状态）
// ============================================================
internal static class SupabaseMemorySchemaManager
{
    private static readonly ConcurrentDictionary<string, bool> Initialized = new();

    internal static void EnsureInitialized(NpgsqlDataSource dataSource, SupabaseMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        // If all switches are off, skip directly.
        if (!options.AutoCreateVectorExtension &&
            !options.AutoCreateSchema &&
            !options.AutoCreateTables &&
            !options.AutoCreateIndexes &&
            !options.LockDownPublicAccess &&
            !options.EnableRowLevelSecurity)
        {
            return;
        }

        if (options.VectorDimensions <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(SupabaseMemoryOptions)}.{nameof(SupabaseMemoryOptions.VectorDimensions)} must be > 0.");
        }

        var key = BuildKey(dataSource, options);
        if (!Initialized.TryAdd(key, true))
        {
            return;
        }

        try
        {
            using var conn = dataSource.OpenConnection();
            ExecuteStatements(conn, SupabaseMemorySchemaScript.BuildStatements(options));
        }
        catch
        {
            // Initialization failure allows retry.
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

    private static string BuildKey(NpgsqlDataSource dataSource, SupabaseMemoryOptions options)
    {
        // Construct key using DataSource identity hash + schema/table configuration.
        // Purpose: avoid duplicate DDL within the same process, and allow multiple data sources to coexist.
        var dsKey = RuntimeHelpers.GetHashCode(dataSource);

        return string.Join(
            "|",
            dsKey.ToString(),
            options.Schema,
            options.MemoryEntriesTable,
            options.MemoryVectorsTable,
            options.VectorDimensions.ToString(),
            options.DistanceMetric.ToString(),
            options.EnableFullTextSearch.ToString(),
            options.FtsRegConfig,
            options.AutoCreateVectorExtension.ToString(),
            options.AutoCreateSchema.ToString(),
            options.AutoCreateTables.ToString(),
            options.AutoCreateIndexes.ToString(),
            options.LockDownPublicAccess.ToString(),
            options.EnableRowLevelSecurity.ToString(),
            options.ForceRowLevelSecurity.ToString(),
            options.CreateServiceRolePolicies.ToString());
    }
}


