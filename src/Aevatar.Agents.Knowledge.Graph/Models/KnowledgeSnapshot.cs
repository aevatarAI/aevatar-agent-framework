namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Complete snapshot of a knowledge graph for a session.
/// </summary>
public sealed class KnowledgeSnapshot
{
    /// <summary>Session this snapshot belongs to.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>All nodes in the graph.</summary>
    public IReadOnlyList<KnowledgeNode> Nodes { get; init; } = [];

    /// <summary>All edges in the graph.</summary>
    public IReadOnlyList<KnowledgeEdge> Edges { get; init; } = [];

    /// <summary>Total number of nodes.</summary>
    public int NodeCount => Nodes.Count;

    /// <summary>Total number of edges.</summary>
    public int EdgeCount => Edges.Count;
}
