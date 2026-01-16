namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Complete snapshot of a knowledge graph for a session.
/// Contains both PlanNodes and KnowledgeNodes along with their edges.
/// </summary>
public class GraphSnapshot
{
    /// <summary>Session this snapshot belongs to.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>All plan nodes in the graph.</summary>
    public IReadOnlyList<PlanNode> PlanNodes { get; init; } = [];

    /// <summary>All knowledge nodes in the graph.</summary>
    public IReadOnlyList<KnowledgeNode> KnowledgeNodes { get; init; } = [];

    /// <summary>All edges in the graph.</summary>
    public IReadOnlyList<IGraphEdge> Edges { get; init; } = [];

    /// <summary>Total number of plan nodes.</summary>
    public int PlanNodeCount => PlanNodes.Count;

    /// <summary>Total number of knowledge nodes.</summary>
    public int KnowledgeNodeCount => KnowledgeNodes.Count;

    /// <summary>Total number of all nodes.</summary>
    public int NodeCount => PlanNodeCount + KnowledgeNodeCount;

    /// <summary>Total number of edges.</summary>
    public int EdgeCount => Edges.Count;

    /// <summary>All nodes as IGraphNode (for generic operations).</summary>
    public IEnumerable<IGraphNode> AllNodes =>
        PlanNodes.Cast<IGraphNode>().Concat(KnowledgeNodes);

    /// <summary>
    /// Gets a node by ID, checking both PlanNodes and KnowledgeNodes.
    /// </summary>
    public IGraphNode? GetNode(string nodeId)
    {
        var plan = PlanNodes.FirstOrDefault(n => n.Id == nodeId);
        if (plan != null) return plan;
        return KnowledgeNodes.FirstOrDefault(n => n.Id == nodeId);
    }

    /// <summary>
    /// Gets all edges where the given node is the source (FromId).
    /// </summary>
    public IEnumerable<IGraphEdge> GetOutgoingEdges(string nodeId) =>
        Edges.Where(e => e.FromId == nodeId);

    /// <summary>
    /// Gets all edges where the given node is the target (ToId).
    /// </summary>
    public IEnumerable<IGraphEdge> GetIncomingEdges(string nodeId) =>
        Edges.Where(e => e.ToId == nodeId);

    /// <summary>
    /// Gets nodes that the given node depends on (follows DependsOn relationships).
    /// </summary>
    public IEnumerable<string> GetDependencies(string nodeId)
    {
        var node = GetNode(nodeId);
        return node?.DependsOn ?? [];
    }

    /// <summary>
    /// Gets nodes that depend on the given node.
    /// </summary>
    public IEnumerable<string> GetDependents(string nodeId) =>
        Edges
            .Where(e => e.ToId == nodeId &&
                       (e.Type == EdgeType.KnowledgeDependsOnKnowledge ||
                        e.Type == EdgeType.PlanDependsOnPlan))
            .Select(e => e.FromId)
            .Distinct();

    /// <summary>
    /// Gets knowledge nodes produced by a plan node.
    /// </summary>
    public IEnumerable<string> GetKnowledgeMotivatedByPlan(string planNodeId) =>
        Edges
            .Where(e => e.ToId == planNodeId && e.Type == EdgeType.KnowledgeMotivatedByPlan)
            .Select(e => e.FromId)
            .Distinct();

    /// <summary>
    /// Gets the full upstream chain of dependencies using BFS.
    /// </summary>
    public IReadOnlyList<string> GetFullUpstreamChain(string nodeId)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        var result = new List<string>();

        var startDeps = GetDependencies(nodeId);
        foreach (var dep in startDeps)
        {
            queue.Enqueue(dep);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            result.Add(current);

            var deps = GetDependencies(current);
            foreach (var dep in deps)
            {
                if (!visited.Contains(dep))
                {
                    queue.Enqueue(dep);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Creates an empty snapshot for a given session.
    /// </summary>
    public static GraphSnapshot Empty(string sessionId) => new()
    {
        SessionId = sessionId,
        PlanNodes = [],
        KnowledgeNodes = [],
        Edges = []
    };
}
