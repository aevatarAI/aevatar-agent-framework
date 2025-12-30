using MongoDB.Bson.Serialization.Attributes;

namespace Aevatar.Agents.Persistence.MongoDB.Memory.Documents;

public sealed class MemoryVectorDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty; // $"{memoryId}::{entryId}"

    public string MemoryId { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;

    public int ScopeType { get; set; }
    public string ScopeId { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public Dictionary<string, string> Tags { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<float> Embedding { get; set; } = new();
}


