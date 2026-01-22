namespace Aevatar.Agents.Persistence.SQLite.Memory.Options;

/// <summary>
/// SQLite-backed AI Memory options.
/// </summary>
public sealed class SQLiteMemoryOptions
{
    // ==============================
    // Table names
    // ==============================

    public string MemoryEntriesTable { get; set; } = "memory_entries";

    public string MemoryVectorsTable { get; set; } = "memory_vectors";

    // ==============================
    // Auto initialization
    // ==============================

    public bool AutoCreateTables { get; set; } = true;

    public bool AutoCreateIndexes { get; set; } = true;
}
