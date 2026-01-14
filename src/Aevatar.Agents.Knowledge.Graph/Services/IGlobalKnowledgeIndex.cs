using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Service for searching and referencing knowledge nodes across sessions.
/// Enables cross-session knowledge sharing and citation.
/// </summary>
public interface IGlobalKnowledgeIndex
{
    /// <summary>
    /// Searches for knowledge nodes across all sessions by keyword/description match.
    /// </summary>
    /// <param name="query">Search query (matched against CoreDescription and DetailedDescription).</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="excludeSessionId">Optional session ID to exclude from results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching knowledge nodes with their global IDs.</returns>
    Task<IReadOnlyList<GlobalKnowledgeNode>> SearchAsync(
        string query,
        int maxResults = 10,
        string? excludeSessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a knowledge node by its global ID (format: sessionId:nodeId).
    /// </summary>
    /// <param name="globalNodeId">The global node ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The knowledge node, or null if not found.</returns>
    Task<GlobalKnowledgeNode?> GetNodeAsync(
        string globalNodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a knowledge node by session ID and node ID.
    /// </summary>
    /// <param name="sessionId">The session containing the node.</param>
    /// <param name="nodeId">The node ID within the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The knowledge node, or null if not found.</returns>
    Task<GlobalKnowledgeNode?> GetNodeAsync(
        string sessionId,
        string nodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a cross-session reference edge from a node in the current session
    /// to a node in another session.
    /// </summary>
    /// <param name="fromSessionId">The session containing the referencing node.</param>
    /// <param name="fromNodeId">The referencing node ID.</param>
    /// <param name="toGlobalNodeId">The global ID of the referenced node (format: sessionId:nodeId).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created edge.</returns>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if either node does not exist.</exception>
    Task<CrossSessionReferenceEdge> CreateCrossSessionReferenceAsync(
        string fromSessionId,
        string fromNodeId,
        string toGlobalNodeId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all cross-session references from a node.
    /// </summary>
    /// <param name="sessionId">The session containing the node.</param>
    /// <param name="nodeId">The node ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of global node IDs that this node references.</returns>
    Task<IReadOnlyList<string>> GetCrossSessionReferencesAsync(
        string sessionId,
        string nodeId,
        CancellationToken ct = default);
}

/// <summary>
/// A knowledge node with global identification for cross-session reference.
/// </summary>
public sealed record GlobalKnowledgeNode
{
    /// <summary>
    /// Global unique identifier (format: sessionId:nodeId).
    /// </summary>
    public required string GlobalId { get; init; }

    /// <summary>
    /// The session this node belongs to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The local node ID within the session.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The underlying knowledge node.
    /// </summary>
    public required KnowledgeNode Node { get; init; }

    /// <summary>
    /// Creates a GlobalKnowledgeNode from a KnowledgeNode.
    /// </summary>
    public static GlobalKnowledgeNode FromNode(KnowledgeNode node)
    {
        return new GlobalKnowledgeNode
        {
            GlobalId = $"{node.SessionId}:{node.Id}",
            SessionId = node.SessionId,
            NodeId = node.Id,
            Node = node
        };
    }

    /// <summary>
    /// Parses a global node ID into session ID and node ID.
    /// </summary>
    /// <param name="globalId">The global ID to parse.</param>
    /// <returns>Tuple of (sessionId, nodeId).</returns>
    /// <exception cref="FormatException">Thrown if the format is invalid.</exception>
    public static (string SessionId, string NodeId) ParseGlobalId(string globalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalId);

        var colonIndex = globalId.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= globalId.Length - 1)
        {
            throw new FormatException(
                $"Invalid global node ID format: '{globalId}'. Expected format: 'sessionId:nodeId'");
        }

        return (globalId[..colonIndex], globalId[(colonIndex + 1)..]);
    }

    /// <summary>
    /// Creates a global ID from session ID and node ID.
    /// </summary>
    public static string CreateGlobalId(string sessionId, string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return $"{sessionId}:{nodeId}";
    }
}
