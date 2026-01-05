namespace Aevatar.Agents.Persistence.MongoDB.Memory.Options;

/// <summary>
/// MongoDB-backed AI Memory options.
///
/// Notes:
/// - Base infra (MongoClient/IMongoDatabase) is provided by Aevatar.Agents.Persistence.MongoDB.
/// - This module provides IMemoryStore + IMemoryVectorIndex implementations.
/// </summary>
public sealed class MongoDbMemoryOptions
{
    /// <summary>
    /// MemoryEntry collection name.
    /// </summary>
    public string MemoryEntriesCollection { get; set; } = "memory_entries";

    /// <summary>
    /// MemoryVectorRecord collection name.
    /// </summary>
    public string MemoryVectorsCollection { get; set; } = "memory_vectors";

    /// <summary>
    /// Optional: create text index for content (for IMemoryStore.SearchAsync).
    /// Default disabled to keep demo simple.
    /// </summary>
    public bool EnsureTextIndexOnContent { get; set; } = false;
}



