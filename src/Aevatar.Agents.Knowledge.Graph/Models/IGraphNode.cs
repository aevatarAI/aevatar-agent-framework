namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Base interface for all graph nodes (PlanNode and KnowledgeNode).
/// Provides common properties shared across node types.
/// </summary>
public interface IGraphNode
{
    /// <summary>
    /// Unique identifier for this node within the session.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The session this node belongs to.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// When this node was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When this node was last updated.
    /// </summary>
    DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Core description - a concise summary of this node.
    /// </summary>
    string CoreDescription { get; }

    /// <summary>
    /// Detailed description - comprehensive explanation.
    /// </summary>
    string DetailedDescription { get; }

    /// <summary>
    /// IDs of nodes this node depends on (upstream dependencies).
    /// </summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>
    /// Owner public key of this node (the agent/user who authored it).
    /// </summary>
    string? Owner { get; }

    /// <summary>
    /// Status of this node with respect to research direction pivots.
    /// </summary>
    PivotNodeStatus PivotStatus { get; }

    /// <summary>
    /// When this node was cancelled due to a pivot (null if not cancelled).
    /// </summary>
    DateTimeOffset? CancelledAt { get; }

    /// <summary>
    /// The pivot operation ID that cancelled this node (null if not cancelled).
    /// </summary>
    string? CancelledByPivotId { get; }

    /// <summary>
    /// Research direction context when this node was created.
    /// </summary>
    string? DirectionContext { get; }

    /// <summary>
    /// Generates a detailed explanation of this node including its relationships.
    /// Returns a NodeExplanation with markdown content suitable for display.
    /// </summary>
    /// <param name="snapshot">The graph snapshot containing all nodes and edges for context.</param>
    /// <returns>A NodeExplanation containing all relevant information about this node.</returns>
    NodeExplanation Explain(GraphSnapshot snapshot);
}
