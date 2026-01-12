namespace Aevatar.Agents.Persistence.Supabase.Memory.Options;

// ============================================================
//  SupabaseMemoryOptions
//
//  Purpose:
//  - Use Supabase(Postgres) to host AI Memory:
//    - MemoryEntry (IMemoryStore)
//    - MemoryVectorRecord (IMemoryVectorIndex via pgvector)
//
//  Principles:
//  - Decouple from Agent State/Config Supabase persistence (different schema/table)
//  - Idempotent initialization (IF NOT EXISTS)
//  - Default minimal exposure (optional LockDown/RLS)
// ============================================================
public sealed class SupabaseMemoryOptions
{
    // ==============================
    // Connection / Schema
    // ==============================

    /// <summary>
    /// Postgres connection string (Supabase Dashboard -> Project Settings -> Database).
    /// Only required when NpgsqlDataSource is not registered elsewhere.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Schema for AI Memory tables. Default uses an independent schema to avoid mixing with agent state/config.
    /// </summary>
    public string Schema { get; set; } = "aevatar_memory";

    // ==============================
    // Table names
    // ==============================

    public string MemoryEntriesTable { get; set; } = "memory_entries";

    public string MemoryVectorsTable { get; set; } = "memory_vectors";

    // ==============================
    // Vector settings
    // ==============================

    /// <summary>
    /// Required: embedding dimensions (drives vector(dims) column + index creation).
    /// </summary>
    public int VectorDimensions { get; set; }

    /// <summary>
    /// Distance metric for vector search (drives operator/opclass).
    /// Default: Cosine.
    /// </summary>
    public SupabaseVectorDistanceMetric DistanceMetric { get; set; } = SupabaseVectorDistanceMetric.Cosine;

    /// <summary>
    /// Optional: auto-create pgvector extension (CREATE EXTENSION IF NOT EXISTS vector).
    /// Default: false (many Supabase projects enable it manually).
    /// </summary>
    public bool AutoCreateVectorExtension { get; set; } = false;

    // ==============================
    // Text search (optional)
    // ==============================

    /// <summary>
    /// Whether to create and use Postgres full-text search for IMemoryStore.SearchAsync.
    /// Default: false (fallback will use ILIKE/substring matching).
    /// </summary>
    public bool EnableFullTextSearch { get; set; } = false;

    /// <summary>
    /// Postgres regconfig for FTS (e.g., simple/english).
    /// Only used when EnableFullTextSearch=true.
    /// </summary>
    public string FtsRegConfig { get; set; } = "simple";

    // ==============================
    // Auto initialization
    // ==============================

    public bool AutoCreateSchema { get; set; } = true;

    public bool AutoCreateTables { get; set; } = true;

    public bool AutoCreateIndexes { get; set; } = true;

    // ==============================
    // Permissions / Exposure control
    // ==============================

    /// <summary>
    /// Whether to tighten permissions by default: revoke PUBLIC/anon/authenticated permissions on schema/table.
    /// </summary>
    public bool LockDownPublicAccess { get; set; } = true;

    /// <summary>
    /// Whether to enable Row Level Security (default disabled to avoid affecting direct server connections).
    /// </summary>
    public bool EnableRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// Whether to force RLS for table owner (FORCE ROW LEVEL SECURITY).
    /// </summary>
    public bool ForceRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// Whether to create \"full access\" policy for Supabase service_role (only when EnableRowLevelSecurity=true).
    /// </summary>
    public bool CreateServiceRolePolicies { get; set; } = false;
}

public enum SupabaseVectorDistanceMetric
{
    Cosine = 0,
    L2 = 1,
    InnerProduct = 2
}


