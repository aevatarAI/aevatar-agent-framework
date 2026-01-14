namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Represents a detailed explanation of a graph node.
/// This is the unified output format for IGraphNode.Explain().
/// </summary>
public sealed record NodeExplanation
{
    /// <summary>
    /// The node ID.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Node type: "Plan" or "Knowledge".
    /// </summary>
    public required string NodeType { get; init; }

    /// <summary>
    /// Human-readable title for the node (from CoreDescription).
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Full Markdown-formatted explanation of this node.
    /// This is the primary content for display.
    /// </summary>
    public required string MarkdownContent { get; init; }

    /// <summary>
    /// IDs of nodes this node directly depends on.
    /// </summary>
    public required IReadOnlyList<string> DirectDependencies { get; init; }

    /// <summary>
    /// IDs of nodes that depend on this node.
    /// </summary>
    public required IReadOnlyList<string> Dependents { get; init; }

    /// <summary>
    /// When this node was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When this node was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
