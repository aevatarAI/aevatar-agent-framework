using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB configuration document wrapper.
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
internal class AgentConfigDocument<TConfig>
{
    /// <summary>
    /// MongoDB document ID (auto-generated).
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// Agent Type (full type name).
    /// </summary>
    [BsonRequired]
    public string AgentType { get; set; } = default!;

    /// <summary>
    /// Agent ID.
    /// </summary>
    [BsonRequired]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Configuration object.
    /// </summary>
    public TConfig Config { get; set; } = default!;

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}


