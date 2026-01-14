namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Utility for detecting cycles in a directed graph using DFS.
/// Used to validate that adding edges won't create cycles in the DAG.
/// </summary>
public static class CycleDetector
{
    /// <summary>
    /// Checks if adding an edge from sourceId to targetId would create a cycle.
    /// </summary>
    /// <param name="sourceId">The source node ID (the dependent).</param>
    /// <param name="targetId">The target node ID (the dependency).</param>
    /// <param name="existingEdges">Dictionary of node ID to its dependencies.</param>
    /// <returns>True if adding this edge would create a cycle; false otherwise.</returns>
    public static bool WouldCreateCycle(
        string sourceId,
        string targetId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingEdges)
    {
        // Self-loop check
        if (sourceId == targetId)
        {
            return true;
        }

        // Check if targetId can reach sourceId through existing edges
        // If so, adding sourceId -> targetId would create a cycle
        var visited = new HashSet<string>();
        return CanReach(targetId, sourceId, existingEdges, visited);
    }

    /// <summary>
    /// Validates that adding multiple edges won't create any cycles.
    /// </summary>
    /// <param name="newEdges">List of (sourceId, targetId) tuples representing edges to add.</param>
    /// <param name="existingEdges">Dictionary of node ID to its dependencies.</param>
    /// <returns>The first edge that would create a cycle, or null if all edges are valid.</returns>
    public static (string Source, string Target)? FindCycleCreatingEdge(
        IEnumerable<(string Source, string Target)> newEdges,
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingEdges)
    {
        // Build a mutable copy of edges for incremental validation
        var edges = new Dictionary<string, List<string>>();
        foreach (var (nodeId, deps) in existingEdges)
        {
            edges[nodeId] = [..deps];
        }

        foreach (var (source, target) in newEdges)
        {
            var readOnlyEdges = edges.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value);

            if (WouldCreateCycle(source, target, readOnlyEdges))
            {
                return (source, target);
            }

            // Add edge to our working copy for subsequent checks
            if (!edges.TryGetValue(source, out var deps))
            {
                deps = [];
                edges[source] = deps;
            }
            deps.Add(target);
        }

        return null;
    }

    /// <summary>
    /// Performs topological sort on the graph.
    /// </summary>
    /// <param name="nodeIds">All node IDs to sort.</param>
    /// <param name="edges">Dictionary of node ID to its dependencies.</param>
    /// <returns>Topologically sorted list (dependencies come before dependents), or null if cycle detected.</returns>
    public static IReadOnlyList<string>? TopologicalSort(
        IEnumerable<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>(); // Currently in DFS stack

        foreach (var nodeId in nodeIds)
        {
            if (!TopologicalSortDfs(nodeId, edges, visited, visiting, result))
            {
                return null; // Cycle detected
            }
        }

        return result;
    }

    private static bool CanReach(
        string from,
        string to,
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
        HashSet<string> visited)
    {
        if (from == to)
        {
            return true;
        }

        if (!visited.Add(from))
        {
            return false; // Already visited
        }

        if (!edges.TryGetValue(from, out var dependencies))
        {
            return false;
        }

        foreach (var dep in dependencies)
        {
            if (CanReach(dep, to, edges, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TopologicalSortDfs(
        string nodeId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> edges,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> result)
    {
        if (visited.Contains(nodeId))
        {
            return true; // Already processed
        }

        if (visiting.Contains(nodeId))
        {
            return false; // Cycle detected
        }

        visiting.Add(nodeId);

        if (edges.TryGetValue(nodeId, out var dependencies))
        {
            foreach (var dep in dependencies)
            {
                if (!TopologicalSortDfs(dep, edges, visited, visiting, result))
                {
                    return false;
                }
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
        result.Add(nodeId);
        return true;
    }
}
