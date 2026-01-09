namespace Aevatar.Agents.Knowledge.Graph.Exceptions;

/// <summary>
/// Thrown when adding a dependency would create a cycle in the directed acyclic graph (DAG).
/// Knowledge graphs must remain acyclic to maintain valid inference chains.
/// </summary>
public sealed class CycleDetectedException : Exception
{
    /// <summary>
    /// Gets the ID of the node that would be the source of the cycle.
    /// </summary>
    public string FromNodeId { get; }

    /// <summary>
    /// Gets the ID of the node that would be the target of the dependency creating the cycle.
    /// </summary>
    public string ToNodeId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CycleDetectedException"/> class.
    /// </summary>
    /// <param name="fromNodeId">The source node ID.</param>
    /// <param name="toNodeId">The target node ID that would create a cycle.</param>
    public CycleDetectedException(string fromNodeId, string toNodeId)
        : base($"Adding dependency from '{fromNodeId}' to '{toNodeId}' would create a cycle.")
    {
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
    }
}
