namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Result of explaining a knowledge node's dependencies.
/// Contains the complete chain of knowledge leading to the target node.
/// </summary>
public sealed class KnowledgeChain
{
    /// <summary>Session this chain belongs to.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>The target node being explained.</summary>
    public required KnowledgeNode TargetNode { get; init; }

    /// <summary>
    /// All nodes in the dependency chain, ordered by level.
    /// Level 0 is the target node, Level 1 is direct dependencies, etc.
    /// </summary>
    public IReadOnlyList<KnowledgeChainLevel> Levels { get; init; } = [];

    /// <summary>
    /// Flattened list of all unique nodes in the chain (for convenience).
    /// Ordered from target to root dependencies.
    /// </summary>
    public IReadOnlyList<KnowledgeNode> Chain { get; init; } = [];

    /// <summary>Total number of nodes in the chain.</summary>
    public int TotalNodes => Chain.Count;

    /// <summary>Maximum depth of the dependency chain.</summary>
    public int MaxDepth => Levels.Count > 0 ? Levels.Count - 1 : 0;
}

/// <summary>
/// A level in the knowledge dependency chain (BFS layer).
/// </summary>
public sealed class KnowledgeChainLevel
{
    /// <summary>Depth level (0 = target node, 1 = direct deps, etc.)</summary>
    public int Depth { get; init; }

    /// <summary>Nodes at this depth level.</summary>
    public IReadOnlyList<KnowledgeNode> Nodes { get; init; } = [];
}
