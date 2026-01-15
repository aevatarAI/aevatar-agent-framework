using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Knowledge.Graph.Store;

namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Implementation of IGlobalKnowledgeIndex for cross-session knowledge search and reference.
/// </summary>
internal sealed class GlobalKnowledgeIndex : IGlobalKnowledgeIndex
{
    private readonly IKnowledgeGraphStore _store;
    private readonly IKnowledgeGraphClientFactory _clientFactory;

    public GlobalKnowledgeIndex(
        IKnowledgeGraphStore store,
        IKnowledgeGraphClientFactory clientFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GlobalKnowledgeNode>> SearchAsync(
        string query,
        int maxResults = 10,
        string? excludeSessionId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<GlobalKnowledgeNode>();

        var nodes = await _store.SearchKnowledgeNodesGlobalAsync(
            query,
            maxResults,
            excludeSessionId,
            ct);

        return nodes.Select(GlobalKnowledgeNode.FromNode).ToList();
    }

    /// <inheritdoc />
    public async Task<GlobalKnowledgeNode?> GetNodeAsync(
        string globalNodeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(globalNodeId))
            return null;

        try
        {
            var (sessionId, nodeId) = GlobalKnowledgeNode.ParseGlobalId(globalNodeId);
            return await GetNodeAsync(sessionId, nodeId, ct);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GlobalKnowledgeNode?> GetNodeAsync(
        string sessionId,
        string nodeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(nodeId))
            return null;

        var node = await _store.GetKnowledgeNodeGlobalAsync(sessionId, nodeId, ct);
        return node == null ? null : GlobalKnowledgeNode.FromNode(node);
    }

    /// <inheritdoc />
    public async Task<CrossSessionReferenceEdge> CreateCrossSessionReferenceAsync(
        string fromSessionId,
        string fromNodeId,
        string toGlobalNodeId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toGlobalNodeId);

        // Parse the target global ID
        var (toSessionId, toNodeId) = GlobalKnowledgeNode.ParseGlobalId(toGlobalNodeId);

        // Verify the source node exists
        var fromClient = _clientFactory.CreateClient(fromSessionId);
        var fromNode = await fromClient.GetNodeAsync(fromNodeId, ct);
        if (fromNode == null)
        {
            throw new NodeNotFoundException(fromNodeId);
        }

        // Verify the target node exists
        var toNode = await _store.GetKnowledgeNodeGlobalAsync(toSessionId, toNodeId, ct);
        if (toNode == null)
        {
            throw new NodeNotFoundException(toGlobalNodeId);
        }

        // Create the cross-session reference edge
        var edge = new CrossSessionReferenceEdge
        {
            SessionId = fromSessionId,
            FromId = fromNodeId,
            ToId = toGlobalNodeId,
            ReferencedSessionId = toSessionId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Store the edge using the standard edge mechanism
        // We use a KnowledgeEdge wrapper for compatibility with existing storage
        var legacyEdge = new KnowledgeEdge
        {
            FromId = fromNodeId,
            ToId = toGlobalNodeId,
            SessionId = fromSessionId,
            CreatedAt = edge.CreatedAt
        };

        await _store.AddEdgeAsync(legacyEdge, fromSessionId, ct);

        return edge;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCrossSessionReferencesAsync(
        string sessionId,
        string nodeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(nodeId))
            return Array.Empty<string>();

        var dependencies = await _store.GetDependenciesAsync(sessionId, nodeId, ct);

        // Filter to only cross-session references (those containing ':' in the target ID)
        return dependencies
            .Where(depId => depId.Contains(':'))
            .ToList();
    }
}
