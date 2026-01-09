using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph;

/// <summary>
/// Knowledge graph client interface for scientific research assistants.
/// Each client instance is scoped to a specific session for isolation.
/// </summary>
public interface IKnowledgeGraphClient
{
    /// <summary>
    /// The session ID this client operates on.
    /// All operations are isolated to this session.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Adds a knowledge node to the graph.
    /// If resourceFolderPath points to a local folder, it will be zipped and uploaded to S3.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node within this session.</param>
    /// <param name="nodeType">Type of knowledge (e.g., MathAxiom, BiologyExperiment).</param>
    /// <param name="coreDescription">Core description - concise summary of the key conclusion.</param>
    /// <param name="detailedDescription">Detailed description - comprehensive explanation.</param>
    /// <param name="proof">Optional proof in Markdown format.</param>
    /// <param name="resourceFolderPath">Optional local folder path containing resources to zip and upload to S3.</param>
    /// <param name="dependsOn">Optional IDs of existing nodes this node depends on (inference relationship).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created node with ResourceUri if folder was uploaded.</returns>
    /// <exception cref="Exceptions.DuplicateNodeException">Thrown if a node with the same ID already exists.</exception>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if any dependency node does not exist.</exception>
    /// <exception cref="Exceptions.CycleDetectedException">Thrown if adding this node would create a cycle.</exception>
    Task<KnowledgeNode> AddNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        string? proof = null,
        string? resourceFolderPath = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete snapshot of the graph (all nodes with full information and edges within current session).
    /// </summary>
    Task<KnowledgeSnapshot> GetKnowledgeSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the complete knowledge chain details for a node by traversing all its upstream dependencies.
    /// Returns both the structured chain and a human-readable Markdown description.
    /// </summary>
    /// <param name="nodeId">The node to get the chain details for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="KnowledgeChainDetails"/> containing:
    /// - The structured <see cref="KnowledgeChain"/> with nodes organized by inference levels
    /// - A Markdown-formatted description as a mini research paper
    /// </returns>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if the node does not exist.</exception>
    Task<KnowledgeChainDetails> GetKnowledgeChainDetailsAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a comprehensive research paper in Markdown format from the entire knowledge snapshot.
    /// Includes all knowledge nodes (CoreDescription, DetailedDescription, Proof, ResourceUri)
    /// organized by inference relationships into a coherent paper structure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Markdown formatted comprehensive research paper.</returns>
    Task<string> GenerateFullPaperAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a node by its ID (within current session).
    /// </summary>
    Task<KnowledgeNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a node from the graph.
    /// Also removes all edges connected to this node and deletes associated S3 files.
    /// </summary>
    Task<bool> RemoveNodeAsync(string nodeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating session-scoped knowledge graph clients.
/// </summary>
public interface IKnowledgeGraphClientFactory
{
    /// <summary>
    /// Creates a knowledge graph client for the specified session.
    /// </summary>
    /// <param name="sessionId">The session ID for isolation.</param>
    /// <returns>A client scoped to the session.</returns>
    IKnowledgeGraphClient CreateClient(string sessionId);
}
