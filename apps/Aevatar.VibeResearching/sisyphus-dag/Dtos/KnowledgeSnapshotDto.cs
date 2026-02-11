namespace SisyphusDag.Dtos;

using System.Text.Json.Serialization;
using SisyphusDag.Models;

/// <summary>
/// Complete snapshot of the knowledge DAG at a point in time.
/// </summary>
public sealed class KnowledgeSnapshotDto
{
    [JsonPropertyName("snapshotTakenAt")]
    public string SnapshotTakenAt { get; set; } = string.Empty;

    [JsonPropertyName("nodes")]
    public List<KnowledgeNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<KnowledgeDependsOnEdge> Edges { get; set; } = [];

    [JsonPropertyName("nodeMap")]
    public Dictionary<string, KnowledgeNode> NodeMap { get; set; } = new();

    [JsonPropertyName("parentsMap")]
    public Dictionary<string, List<string>> ParentsMap { get; set; } = new();

    [JsonPropertyName("childrenMap")]
    public Dictionary<string, List<string>> ChildrenMap { get; set; } = new();
}
