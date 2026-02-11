namespace SisyphusDag.Data;

using SisyphusDag.Models;

/// <summary>
/// Neo4j persistence for knowledge nodes and edges.
/// </summary>
public interface IKnowledgeRepository
{
    /// <summary>
    /// Batch-creates KnowledgeNode nodes via UNWIND.
    /// Each item in nodeParams is a dictionary of property name to value.
    /// </summary>
    Task CreateNodesAsync(
        List<Dictionary<string, object?>> nodeParams,
        CancellationToken ct = default);

    /// <summary>
    /// Batch-creates DEPENDS_ON edges via UNWIND.
    /// Each item has keys: fromId, toId, createdBy, createdAt, updatedBy, updatedAt.
    /// </summary>
    Task CreateEdgesAsync(
        List<Dictionary<string, object?>> edgeParams,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all KnowledgeNode nodes.
    /// </summary>
    Task<List<KnowledgeNode>> GetAllNodesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all DEPENDS_ON edges.
    /// </summary>
    Task<List<KnowledgeDependsOnEdge>> GetAllEdgesAsync(CancellationToken ct = default);

    /// <summary>
    /// Batch-updates KnowledgeNode nodes via UNWIND.
    /// Each item has keys: id plus all updatable fields.
    /// </summary>
    Task UpdateNodesAsync(
        List<Dictionary<string, object?>> updateParams,
        CancellationToken ct = default);

    /// <summary>
    /// Batch-deletes KnowledgeNode nodes (DETACH DELETE) via UNWIND.
    /// </summary>
    Task DeleteNodesAsync(List<string> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns a single KnowledgeNode by ID, or null if not found.
    /// </summary>
    Task<KnowledgeNode?> GetNodeByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of nodes that the given node depends on (upstream/parent).
    /// Direction: (node)-[:DEPENDS_ON]->(parent), returns parent IDs.
    /// </summary>
    Task<List<string>> GetParentIdsAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of nodes that depend on the given node (downstream/children).
    /// Direction: (child)-[:DEPENDS_ON]->(node), returns child IDs.
    /// </summary>
    Task<List<string>> GetChildIdsAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Returns KnowledgeNodes for the given list of IDs.
    /// Nodes that do not exist are silently omitted from the result.
    /// </summary>
    Task<List<KnowledgeNode>> GetNodesByIdsAsync(List<string> ids, CancellationToken ct = default);
}
