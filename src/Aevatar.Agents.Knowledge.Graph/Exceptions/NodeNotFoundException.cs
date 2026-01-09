namespace Aevatar.Agents.Knowledge.Graph.Exceptions;

/// <summary>
/// Thrown when a referenced knowledge node does not exist in the graph.
/// </summary>
public sealed class NodeNotFoundException : Exception
{
    /// <summary>
    /// Gets the ID of the node that was not found.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeNotFoundException"/> class.
    /// </summary>
    /// <param name="nodeId">The ID of the node that was not found.</param>
    public NodeNotFoundException(string nodeId)
        : base($"Node '{nodeId}' not found.")
    {
        NodeId = nodeId;
    }
}
