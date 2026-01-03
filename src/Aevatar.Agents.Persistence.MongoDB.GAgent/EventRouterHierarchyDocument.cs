using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent;

/// <summary>
/// MongoDB document for EventRouter hierarchy.
/// </summary>
public class EventRouterHierarchyDocument
{
    [BsonId]
    public string AgentId { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public List<string> ChildrenIds { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


