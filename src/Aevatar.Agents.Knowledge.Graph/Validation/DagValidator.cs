using Aevatar.Agents.Knowledge.Graph.Store;

namespace Aevatar.Agents.Knowledge.Graph.Validation;

/// <summary>
/// Validates that the knowledge graph maintains DAG (Directed Acyclic Graph) properties.
/// </summary>
internal sealed class DagValidator
{
    private readonly IKnowledgeGraphStore _store;

    public DagValidator(IKnowledgeGraphStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Checks if a node exists in the graph.
    /// </summary>
    public Task<bool> NodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken = default)
    {
        return _store.NodeExistsAsync(sessionId, nodeId, cancellationToken);
    }

    /// <summary>
    /// Validates that adding the specified dependencies would not create a cycle.
    /// Uses BFS to check if any dependency can reach the new node.
    /// </summary>
    public async Task<bool> ValidateNoCycleAsync(
        string sessionId,
        string newNodeId,
        IEnumerable<string> dependsOnIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var depId in dependsOnIds)
        {
            if (await CanReachAsync(sessionId, depId, newNodeId, cancellationToken))
            {
                return false; // Would create cycle
            }
        }
        return true;
    }

    /// <summary>
    /// BFS to check if fromId can reach toId through dependency edges.
    /// </summary>
    private async Task<bool> CanReachAsync(
        string sessionId,
        string fromId,
        string toId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(fromId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, toId, StringComparison.Ordinal)) return true;
            if (!visited.Add(current)) continue;

            var deps = await _store.GetDependenciesAsync(sessionId, current, cancellationToken);
            foreach (var dep in deps)
            {
                if (!visited.Contains(dep))
                {
                    queue.Enqueue(dep);
                }
            }
        }

        return false;
    }
}
