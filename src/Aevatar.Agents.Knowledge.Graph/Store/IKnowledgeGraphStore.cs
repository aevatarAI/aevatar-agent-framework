using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Store;

/// <summary>
/// Internal interface for knowledge graph storage.
/// All operations are scoped to a session ID for isolation.
/// </summary>
internal interface IKnowledgeGraphStore
{
    Task AddNodeAsync(KnowledgeNode node, CancellationToken cancellationToken);
    Task<KnowledgeNode?> GetNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeNode>> GetAllNodesAsync(string sessionId, CancellationToken cancellationToken);
    Task<bool> RemoveNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task<bool> NodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken);

    Task AddEdgeAsync(KnowledgeEdge edge, string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeEdge>> GetAllEdgesAsync(string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetDependenciesAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
    Task RemoveEdgesForNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken);
}
