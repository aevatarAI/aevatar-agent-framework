using Aevatar.Agents.Persistence.SQLite.Internal;
using Aevatar.Agents.Persistence.SQLite.Memory.Options;

namespace Aevatar.Agents.Persistence.SQLite.Memory.Setup;

/// <summary>
/// Generate SQLite initialization SQL for AI Memory (tables / indexes).
/// </summary>
public static class SQLiteMemorySchemaScript
{
    public static IReadOnlyList<string> BuildStatements(SQLiteMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var entries = SQLiteSql.Ident(options.MemoryEntriesTable, nameof(options.MemoryEntriesTable));
        var vectors = SQLiteSql.Ident(options.MemoryVectorsTable, nameof(options.MemoryVectorsTable));

        var stmts = new List<string>(12);

        if (options.AutoCreateTables)
        {
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {entries} (
  memory_id TEXT NOT NULL,
  entry_id TEXT NOT NULL,
  scope_type INTEGER NOT NULL,
  scope_id TEXT NOT NULL,
  run_id TEXT NOT NULL,
  agent_id TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  tags TEXT NOT NULL,
  created_at INTEGER NOT NULL DEFAULT (strftime('%s','now')),
  PRIMARY KEY (memory_id, entry_id)
)");

            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {vectors} (
  memory_id TEXT NOT NULL,
  entry_id TEXT NOT NULL,
  scope_type INTEGER NOT NULL,
  scope_id TEXT NOT NULL,
  run_id TEXT NOT NULL,
  agent_id TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  tags TEXT NOT NULL,
  created_at INTEGER NOT NULL DEFAULT (strftime('%s','now')),
  embedding TEXT NOT NULL,
  PRIMARY KEY (memory_id, entry_id)
)");
        }

        if (options.AutoCreateIndexes)
        {
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{entries}_memory_created ON {entries} (memory_id, created_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{vectors}_memory_created ON {vectors} (memory_id, created_at DESC)");
        }

        return stmts
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string BuildSql(SQLiteMemoryOptions options)
    {
        var stmts = BuildStatements(options);
        return string.Join(";\n\n", stmts) + ";\n";
    }
}
