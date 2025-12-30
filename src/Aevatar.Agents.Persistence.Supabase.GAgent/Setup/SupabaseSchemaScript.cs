using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.Internal;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.Setup;

/// <summary>
/// Generate Supabase(Postgres) initialization SQL (create schema / tables / indexes / tighten permissions / RLS).
/// This class is used for both:
/// - Runtime auto-initialization (executed by <see cref="SupabaseSchemaManager"/>)
/// - Output SQL files for DBA/ops audit and manual deployment
/// </summary>
public static class SupabaseSchemaScript
{
    public static IReadOnlyList<string> BuildStatements(SupabasePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Perform strict validation first to avoid concatenating unsafe identifiers into SQL.
        var schema = SupabaseSql.Ident(options.Schema, nameof(options.Schema));

        var states = SupabaseSql.Ident(options.AgentStatesTable, nameof(options.AgentStatesTable));
        var configs = SupabaseSql.Ident(options.AgentConfigsTable, nameof(options.AgentConfigsTable));
        var routers = SupabaseSql.Ident(options.EventRouterHierarchiesTable, nameof(options.EventRouterHierarchiesTable));

        var stmts = new List<string>(32);

        // ==============================
        // Schema
        // ==============================
        if (options.AutoCreateSchema)
        {
            stmts.Add($"CREATE SCHEMA IF NOT EXISTS {schema}");
        }

        // ==============================
        // Tables
        // ==============================
        if (options.AutoCreateTables)
        {
            // -------- Agent States --------
            // Use (state_type, agent_id) as primary key, allowing same agent_id to coexist under different state types.
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{states} (
  state_type text NOT NULL,
  agent_id text NOT NULL,
  state_data bytea NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (state_type, agent_id)
)");

            // -------- Agent Configs --------
            // config_type: Full type name of TConfig, used to isolate different configuration types.
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{configs} (
  config_type text NOT NULL,
  agent_type text NOT NULL,
  agent_id text NOT NULL,
  config_data jsonb NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (config_type, agent_type, agent_id)
)");

            // -------- EventRouter Hierarchies --------
            // children_ids stored as text[]; query patterns mainly by agent_id / parent_id.
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{routers} (
  agent_id text PRIMARY KEY,
  parent_id text NULL,
  children_ids text[] NOT NULL DEFAULT '{{}}'::text[],
  updated_at timestamptz NOT NULL DEFAULT now()
)");
        }

        // ==============================
        // Indexes
        // ==============================
        if (options.AutoCreateIndexes)
        {
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_updated_at ON {schema}.{states} (updated_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_agent_version ON {schema}.{states} (agent_id, version)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{configs}_updated_at ON {schema}.{configs} (updated_at DESC)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_parent_id ON {schema}.{routers} (parent_id)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_updated_at ON {schema}.{routers} (updated_at DESC)");
        }

        // ==============================
        // Permissions (Lock down)
        // ==============================
        if (options.LockDownPublicAccess)
        {
            // 1) Schema level: revoke PUBLIC usage permissions to avoid accidental exposure by PostgREST
            stmts.Add($"REVOKE ALL ON SCHEMA {schema} FROM PUBLIC");

            // 2) Table level: revoke PUBLIC permissions
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{states} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{configs} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{routers} FROM PUBLIC");

            // 3) Supabase common roles: anon/authenticated may not exist, so use DO block for existence check
            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{states} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{configs} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{routers} FROM anon';
  END IF;
END
$$");

            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{states} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{configs} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{routers} FROM authenticated';
  END IF;
END
$$");
        }

        // ==============================
        // RLS (optional)
        // ==============================
        if (options.EnableRowLevelSecurity)
        {
            stmts.Add($"ALTER TABLE {schema}.{states} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{configs} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{routers} ENABLE ROW LEVEL SECURITY");

            if (options.ForceRowLevelSecurity)
            {
                stmts.Add($"ALTER TABLE {schema}.{states} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{configs} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{routers} FORCE ROW LEVEL SECURITY");
            }

            // Optional: create full-access policy for Supabase service_role (when PostgREST/JWT role=service_role).
            if (options.CreateServiceRolePolicies)
            {
                stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'service_role') THEN
    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_states ON {schema}.{states}';
    EXECUTE 'CREATE POLICY aevatar_service_all_states ON {schema}.{states} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_configs ON {schema}.{configs}';
    EXECUTE 'CREATE POLICY aevatar_service_all_configs ON {schema}.{configs} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_routers ON {schema}.{routers}';
    EXECUTE 'CREATE POLICY aevatar_service_all_routers ON {schema}.{routers} FOR ALL TO service_role USING (true) WITH CHECK (true)';
  END IF;
END
$$");
            }
        }

        // Finally trim whitespace again to ensure execution side doesn't get empty SQL.
        return stmts
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string BuildSql(SupabasePersistenceOptions options)
    {
        var stmts = BuildStatements(options);
        return string.Join(";\n\n", stmts) + ";\n";
    }
}


