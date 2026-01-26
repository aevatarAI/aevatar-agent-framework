using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Aevatar.Agents.Persistence.SQLite.Internal;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Setup;

/// <summary>
/// Generate SQLite initialization SQL (create tables / indexes).
/// </summary>
public static class SQLiteSchemaScript
{
    public static IReadOnlyList<string> BuildStatements(SQLitePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var states = SQLiteSql.Ident(options.AgentStatesTable, nameof(options.AgentStatesTable));
        var configs = SQLiteSql.Ident(options.AgentConfigsTable, nameof(options.AgentConfigsTable));
        var routers = SQLiteSql.Ident(options.EventRouterHierarchiesTable, nameof(options.EventRouterHierarchiesTable));

        var stmts = new List<string>(16);

        if (options.AutoCreateTables)
        {
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {states} (
  state_type TEXT NOT NULL,
  agent_id TEXT NOT NULL,
  state_data BLOB NOT NULL,
  version INTEGER NOT NULL DEFAULT 1,
  updated_at INTEGER NOT NULL DEFAULT (strftime('%s','now')),
  PRIMARY KEY (state_type, agent_id)
)");

            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {configs} (
  config_type TEXT NOT NULL,
  agent_type TEXT NOT NULL,
  agent_id TEXT NOT NULL,
  config_data TEXT NOT NULL,
  updated_at INTEGER NOT NULL DEFAULT (strftime('%s','now')),
  PRIMARY KEY (config_type, agent_type, agent_id)
)");

            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {routers} (
  agent_id TEXT PRIMARY KEY,
  parent_id TEXT NULL,
  children_ids TEXT NOT NULL DEFAULT '[]',
  updated_at INTEGER NOT NULL DEFAULT (strftime('%s','now'))
)");
        }

        if (options.AutoCreateIndexes)
        {
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_updated_at ON {states} (updated_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_agent_version ON {states} (agent_id, version)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{configs}_updated_at ON {configs} (updated_at DESC)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_parent_id ON {routers} (parent_id)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_updated_at ON {routers} (updated_at DESC)");
        }

        return stmts
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string BuildSql(SQLitePersistenceOptions options)
    {
        var stmts = BuildStatements(options);
        return string.Join(";\n\n", stmts) + ";\n";
    }
}
