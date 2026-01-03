namespace Aevatar.Agents.Persistence.Supabase.GAgent.Options;

/// <summary>
/// Supabase(Postgres) persistence options for GAgent State/Config/EventRouter.
///
/// Design goals:
/// - Use Supabase-hosted Postgres as storage backend (direct database connection)
/// - Auto-create tables/indexes (can be disabled)
/// - Default \"minimal exposure\": try to avoid being directly exposed by Supabase PostgREST (configurable)
/// </summary>
public sealed class SupabasePersistenceOptions
{
    /// <summary>
    /// Postgres connection string (from Supabase Dashboard -> Project Settings -> Database).
    ///
    /// Note:
    /// - In the split architecture, the connection pool is owned by the base project
    ///   (<see cref="Aevatar.Agents.Persistence.Supabase.DependencyInjection.SupabaseServiceCollectionExtensions"/>),
    ///   so this field is mainly kept for convenience/compatibility.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Schema where storage resides. Default uses independent schema to avoid mixing with public/business tables.
    /// </summary>
    public string Schema { get; set; } = "aevatar";

    // ==============================
    // Table names (recommended: all lowercase + underscore)
    // ==============================

    public string AgentStatesTable { get; set; } = "agent_states";

    public string AgentConfigsTable { get; set; } = "agent_configs";

    public string EventRouterHierarchiesTable { get; set; } = "agent_event_router_hierarchies";

    // ==============================
    // Auto initialization
    // ==============================

    /// <summary>
    /// Whether to auto-create Schema (if not exists).
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// Whether to auto-create tables (if not exists).
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>
    /// Whether to auto-create indexes (if not exists).
    /// </summary>
    public bool AutoCreateIndexes { get; set; } = true;

    // ==============================
    // Permissions / Exposure control
    // ==============================

    /// <summary>
    /// Whether to tighten permissions by default: revoke PUBLIC/anon/authenticated permissions on schema/table.
    ///
    /// Note:
    /// - This does not affect table owner (usually the database user used for initialization/server connection).
    /// - Purpose: prevent direct read/write via anon key when Supabase exposes PostgREST by default.
    /// </summary>
    public bool LockDownPublicAccess { get; set; } = true;

    /// <summary>
    /// Whether to enable Row Level Security (default disabled to avoid affecting direct server connections).
    ///
    /// Recommendation:
    /// - If you plan to expose these tables via Supabase PostgREST, enable RLS and configure Policy.
    /// - This library only provides the most basic \"service_role full access\" policy (optional).
    /// </summary>
    public bool EnableRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// Whether to force RLS for table owner (FORCE ROW LEVEL SECURITY).
    /// Default disabled to avoid server direct connection (owner) being blocked by RLS.
    /// </summary>
    public bool ForceRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// Whether to create \"full access\" Policy for Supabase's service_role.
    /// Only effective when EnableRowLevelSecurity=true.
    /// </summary>
    public bool CreateServiceRolePolicies { get; set; } = false;
}


