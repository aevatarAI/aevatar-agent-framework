namespace Aevatar.Agents.Persistence.SQLite.GAgent.Options;

/// <summary>
/// SQLite persistence options for GAgent State/Config/EventRouter.
/// </summary>
public sealed class SQLitePersistenceOptions
{
    // ==============================
    // Table names (lowercase + underscore recommended)
    // ==============================

    public string AgentStatesTable { get; set; } = "agent_states";

    public string AgentConfigsTable { get; set; } = "agent_configs";

    public string EventRouterHierarchiesTable { get; set; } = "agent_event_router_hierarchies";

    // ==============================
    // Auto initialization
    // ==============================

    public bool AutoCreateTables { get; set; } = true;

    public bool AutoCreateIndexes { get; set; } = true;
}
