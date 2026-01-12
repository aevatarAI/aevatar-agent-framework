using System;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB document for agent state storage.
/// Uses Protobuf byte[] for state data.
/// </summary>
internal class AgentStateDocument
{
    /// <summary>
    /// Agent ID (MongoDB _id).
    /// </summary>
    [BsonId]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// State data serialized as Protobuf bytes.
    /// </summary>
    public byte[] StateData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// State type full name for auditing/debugging.
    /// </summary>
    public string StateType { get; set; } = string.Empty;

    /// <summary>
    /// Version for optimistic concurrency (event version at snapshot time).
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}


