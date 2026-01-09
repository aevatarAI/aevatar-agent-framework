namespace Aevatar.Agents.Knowledge.Graph.Exceptions;

/// <summary>
/// Thrown when attempting to add a node with an ID that already exists.
/// </summary>
public sealed class DuplicateNodeException : Exception
{
    /// <summary>
    /// Gets the ID of the duplicate node.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateNodeException"/> class.
    /// </summary>
    /// <param name="nodeId">The ID of the duplicate node.</param>
    public DuplicateNodeException(string nodeId)
        : base($"A node with ID '{nodeId}' already exists.")
    {
        NodeId = nodeId;
    }
}
