using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;

namespace Aevatar.Agents.Persistence.Supabase.Memory.Setup;

// ============================================================
//  SupabaseMemorySchemaScript
//
//  PURPOSE:
//  - Generate Supabase(Postgres) initialization SQL:
//    - create schema
//    - create tables
//    - create indexes (FTS + pgvector ivfflat)
//    - tighten permissions / optional RLS
//
//  NOTE:
//  - Strong identifier validation is mandatory (prevent SQL injection).
// ============================================================
public static class SupabaseMemorySchemaScript
{
    public static IReadOnlyList<string> BuildStatements(SupabaseMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.VectorDimensions <= 0)
        {
            throw new ArgumentException(
                $"{nameof(SupabaseMemoryOptions.VectorDimensions)} must be > 0.",
                nameof(options));
        }

        var schema = SupabaseSql.Ident(options.Schema, nameof(options.Schema));
        var entries = SupabaseSql.Ident(options.MemoryEntriesTable, nameof(options.MemoryEntriesTable));
        var vectors = SupabaseSql.Ident(options.MemoryVectorsTable, nameof(options.MemoryVectorsTable));

        var stmts = new List<string>(64);

        // ==============================
        // Extensions
        // ==============================
        if (options.AutoCreateVectorExtension)
        {
            // pgvector extension name is "vector"
            stmts.Add("CREATE EXTENSION IF NOT EXISTS vector");
        }

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
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{entries} (
  memory_id text NOT NULL,
  entry_id text NOT NULL,
  scope_type int NOT NULL,
  scope_id text NOT NULL DEFAULT '',
  run_id text NOT NULL DEFAULT '',
  agent_id text NOT NULL DEFAULT '',
  role text NOT NULL DEFAULT '',
  content text NOT NULL,
  tags jsonb NOT NULL DEFAULT '{{}}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (memory_id, entry_id)
)");

            // NOTE:
            // - embedding uses vector(dims) so we must bake dimensions into DDL.
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{vectors} (
  memory_id text NOT NULL,
  entry_id text NOT NULL,
  scope_type int NOT NULL,
  scope_id text NOT NULL DEFAULT '',
  run_id text NOT NULL DEFAULT '',
  agent_id text NOT NULL DEFAULT '',
  role text NOT NULL DEFAULT '',
  content text NOT NULL,
  tags jsonb NOT NULL DEFAULT '{{}}'::jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  embedding vector({options.VectorDimensions}) NOT NULL,
  PRIMARY KEY (memory_id, entry_id)
)");
        }

        // ==============================
        // Indexes
        // ==============================
        if (options.AutoCreateIndexes)
        {
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{entries}_memory_created_at ON {schema}.{entries} (memory_id, created_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{vectors}_memory_created_at ON {schema}.{vectors} (memory_id, created_at DESC)");

            if (options.EnableFullTextSearch)
            {
                var cfg = SupabaseSql.RegConfigLiteral(options.FtsRegConfig);
                stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{entries}_fts ON {schema}.{entries} USING GIN (to_tsvector({cfg}, content))");
            }

            // Vector index (ivfflat) - widely available in pgvector.
            var opClass = GetVectorOpClass(options.DistanceMetric);
            stmts.Add(
                $"CREATE INDEX IF NOT EXISTS idx_{vectors}_embedding_ivfflat ON {schema}.{vectors} USING ivfflat (embedding {opClass}) WITH (lists = 100)");
        }

        // ==============================
        // Permissions (Lock down)
        // ==============================
        if (options.LockDownPublicAccess)
        {
            stmts.Add($"REVOKE ALL ON SCHEMA {schema} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{entries} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{vectors} FROM PUBLIC");

            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{entries} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{vectors} FROM anon';
  END IF;
END
$$");

            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{entries} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{vectors} FROM authenticated';
  END IF;
END
$$");
        }

        // ==============================
        // RLS (optional)
        // ==============================
        if (options.EnableRowLevelSecurity)
        {
            stmts.Add($"ALTER TABLE {schema}.{entries} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{vectors} ENABLE ROW LEVEL SECURITY");

            if (options.ForceRowLevelSecurity)
            {
                stmts.Add($"ALTER TABLE {schema}.{entries} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{vectors} FORCE ROW LEVEL SECURITY");
            }

            if (options.CreateServiceRolePolicies)
            {
                stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'service_role') THEN
    EXECUTE 'DROP POLICY IF EXISTS aevatar_memory_service_all_entries ON {schema}.{entries}';
    EXECUTE 'CREATE POLICY aevatar_memory_service_all_entries ON {schema}.{entries} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_memory_service_all_vectors ON {schema}.{vectors}';
    EXECUTE 'CREATE POLICY aevatar_memory_service_all_vectors ON {schema}.{vectors} FOR ALL TO service_role USING (true) WITH CHECK (true)';
  END IF;
END
$$");
            }
        }

        return stmts
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string BuildSql(SupabaseMemoryOptions options)
    {
        var stmts = BuildStatements(options);
        return string.Join(";\n\n", stmts) + ";\n";
    }

    private static string GetVectorOpClass(SupabaseVectorDistanceMetric metric)
    {
        return metric switch
        {
            SupabaseVectorDistanceMetric.L2 => "vector_l2_ops",
            SupabaseVectorDistanceMetric.InnerProduct => "vector_ip_ops",
            _ => "vector_cosine_ops"
        };
    }
}


