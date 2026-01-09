namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// A directed edge in the knowledge graph (from -> to means "from depends on to").
/// </summary>
public sealed class KnowledgeEdge
{
    /// <summary>Source node ID (the dependent).</summary>
    public required string FromId { get; init; }

    /// <summary>Target node ID (the dependency).</summary>
    public required string ToId { get; init; }

    /// <summary>When this edge was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
