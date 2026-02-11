namespace SisyphusDag.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Input DTO for batch knowledge creation.
/// </summary>
public sealed class KnowledgesDto
{
    [JsonPropertyName("knowledgeList")]
    public List<KnowledgeDto> KnowledgeList { get; set; } = [];

    /// <summary>
    /// Maps parent index to list of child indices.
    /// Edges: (node at parentIndex)-[:DEPENDS_ON]->(node at childIndex).
    /// </summary>
    [JsonPropertyName("indexDependencies")]
    public Dictionary<int, List<int>>? IndexDependencies { get; set; }
}
