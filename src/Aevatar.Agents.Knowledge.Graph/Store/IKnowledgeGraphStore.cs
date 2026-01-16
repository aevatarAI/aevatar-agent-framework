using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Store;

/// <summary>
/// Internal interface for knowledge graph storage.
/// All operations are scoped to a session ID for isolation.
/// </summary>
internal interface IKnowledgeGraphStore
{
    // ========== Session Operations ==========

    /// <summary>
    /// Creates and stores a new session.
    /// </summary>
    Task<Session> CreateSessionAsync(string? sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a session's status and end time.
    /// </summary>
    Task UpdateSessionAsync(Session session, CancellationToken cancellationToken);

    // ========== KnowledgeNode Operations ==========

    Task AddKnowledgeNodeAsync(KnowledgeNode node, CancellationToken cancellationToken);
    Task<KnowledgeNode?> GetKnowledgeNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesAsync(string sessionId, CancellationToken cancellationToken);
    Task<bool> RemoveKnowledgeNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<bool> KnowledgeNodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    // ========== PlanNode Operations ==========

    Task AddPlanNodeAsync(PlanNode node, CancellationToken cancellationToken);
    Task<PlanNode?> GetPlanNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlanNode>> GetAllPlanNodesAsync(string sessionId, CancellationToken cancellationToken);
    Task<bool> RemovePlanNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<bool> PlanNodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task UpdatePlanNodeAsync(PlanNode node, CancellationToken cancellationToken);

    // ========== Generic Node Operations ==========

    /// <summary>
    /// Checks if any node (PlanNode or KnowledgeNode) with the given ID exists.
    /// </summary>
    Task<bool> NodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a node as IGraphNode (can be either PlanNode or KnowledgeNode).
    /// </summary>
    Task<IGraphNode?> GetNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes any node (PlanNode or KnowledgeNode) by ID.
    /// </summary>
    Task<bool> RemoveNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all nodes (both PlanNodes and KnowledgeNodes) in the session.
    /// </summary>
    Task<IReadOnlyList<IGraphNode>> GetAllNodesAsync(string sessionId, CancellationToken cancellationToken);

    // ========== Edge Operations ==========

    Task AddEdgeAsync(KnowledgeEdge edge, string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeEdge>> GetAllEdgesAsync(string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetDependenciesAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task RemoveEdgesForNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    // ========== Cross-Session Operations ==========

    /// <summary>
    /// Searches for knowledge nodes across all sessions by keyword matching.
    /// </summary>
    /// <param name="query">Search query (matched against CoreDescription and DetailedDescription).</param>
    /// <param name="maxResults">Maximum number of results.</param>
    /// <param name="excludeSessionId">Optional session to exclude from results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching knowledge nodes from any session.</returns>
    Task<IReadOnlyList<KnowledgeNode>> SearchKnowledgeNodesGlobalAsync(
        string query,
        int maxResults = 10,
        string? excludeSessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a knowledge node from any session by session ID and node ID.
    /// Unlike GetKnowledgeNodeAsync, this doesn't require the caller to own the session.
    /// </summary>
    Task<KnowledgeNode?> GetKnowledgeNodeGlobalAsync(
        string sessionId,
        string nodeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets ALL knowledge nodes across all sessions (for migration purposes).
    /// </summary>
    Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesGlobalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets ALL plan nodes across all sessions.
    /// </summary>
    Task<IReadOnlyList<PlanNode>> GetAllPlanNodesGlobalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets ALL edges across all sessions (for migration purposes).
    /// </summary>
    Task<IReadOnlyList<(KnowledgeEdge Edge, string SessionId)>> GetAllEdgesGlobalAsync(CancellationToken cancellationToken);
}
