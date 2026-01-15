namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Error codes for graph operations.
/// Used in structured error responses per FR-015c.
/// </summary>
public enum GraphErrorCode
{
    /// <summary>
    /// Referenced node does not exist in the graph.
    /// </summary>
    NodeNotFound,

    /// <summary>
    /// Referenced session does not exist.
    /// </summary>
    SessionNotFound,

    /// <summary>
    /// Adding edge would create a cycle in the DAG.
    /// </summary>
    CycleDetected,

    /// <summary>
    /// PlanNode status change is not allowed (e.g., Completed to Pending).
    /// </summary>
    InvalidStateTransition,

    /// <summary>
    /// Relationship type is not valid for the node types involved.
    /// </summary>
    InvalidRelationshipType,

    /// <summary>
    /// General validation failure.
    /// </summary>
    ValidationError,

    /// <summary>
    /// A node with the same ID already exists.
    /// </summary>
    DuplicateNode,

    /// <summary>
    /// Cannot delete PlanNode that has motivated KnowledgeNodes.
    /// </summary>
    PlanNodeHasKnowledge
}
