using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Exceptions;

/// <summary>
/// Exception thrown when a graph operation fails.
/// Contains structured error information for API responses per FR-015c.
/// </summary>
public class GraphOperationException : Exception
{
    /// <summary>
    /// Gets the error code categorizing this failure.
    /// </summary>
    public GraphErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets additional details about the error (node IDs, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, object> Details { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphOperationException"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Optional additional details.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public GraphOperationException(
        GraphErrorCode errorCode,
        string message,
        IReadOnlyDictionary<string, object>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// Creates a NodeNotFound exception.
    /// </summary>
    public static GraphOperationException NodeNotFound(string nodeId) =>
        new(GraphErrorCode.NodeNotFound,
            $"Node '{nodeId}' not found.",
            new Dictionary<string, object> { ["nodeId"] = nodeId });

    /// <summary>
    /// Creates a SessionNotFound exception.
    /// </summary>
    public static GraphOperationException SessionNotFound(string sessionId) =>
        new(GraphErrorCode.SessionNotFound,
            $"Session '{sessionId}' not found.",
            new Dictionary<string, object> { ["sessionId"] = sessionId });

    /// <summary>
    /// Creates a CycleDetected exception.
    /// </summary>
    public static GraphOperationException CycleDetected(string fromNodeId, string toNodeId) =>
        new(GraphErrorCode.CycleDetected,
            $"Adding dependency from '{fromNodeId}' to '{toNodeId}' would create a cycle.",
            new Dictionary<string, object> { ["fromNodeId"] = fromNodeId, ["toNodeId"] = toNodeId });

    /// <summary>
    /// Creates an InvalidStateTransition exception.
    /// </summary>
    public static GraphOperationException InvalidStateTransition(string nodeId, PlanNodeStatus from, PlanNodeStatus to) =>
        new(GraphErrorCode.InvalidStateTransition,
            $"Cannot transition node '{nodeId}' from {from} to {to}.",
            new Dictionary<string, object> { ["nodeId"] = nodeId, ["fromStatus"] = from.ToString(), ["toStatus"] = to.ToString() });

    /// <summary>
    /// Creates an InvalidRelationshipType exception.
    /// </summary>
    public static GraphOperationException InvalidRelationshipType(RelationshipType type, string fromKind, string toKind) =>
        new(GraphErrorCode.InvalidRelationshipType,
            $"Relationship type {type} is not valid from {fromKind} to {toKind}.",
            new Dictionary<string, object> { ["relationshipType"] = type.ToString(), ["fromKind"] = fromKind, ["toKind"] = toKind });

    /// <summary>
    /// Creates a DuplicateNode exception.
    /// </summary>
    public static GraphOperationException DuplicateNode(string nodeId) =>
        new(GraphErrorCode.DuplicateNode,
            $"Node '{nodeId}' already exists.",
            new Dictionary<string, object> { ["nodeId"] = nodeId });

    /// <summary>
    /// Creates a PlanNodeHasKnowledge exception.
    /// </summary>
    public static GraphOperationException PlanNodeHasKnowledge(string nodeId, IEnumerable<string> knowledgeNodeIds) =>
        new(GraphErrorCode.PlanNodeHasKnowledge,
            $"Cannot delete PlanNode '{nodeId}' because it has motivated KnowledgeNodes.",
            new Dictionary<string, object> { ["nodeId"] = nodeId, ["knowledgeNodeIds"] = knowledgeNodeIds.ToList() });
}
