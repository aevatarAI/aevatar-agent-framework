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

    // ========== Session Management ==========

    /// <summary>
    /// Gets the current session information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current session, or null if session doesn't exist in storage.</returns>
    Task<Session?> GetSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the current session with the specified status.
    /// Once ended, the session cannot be modified further.
    /// </summary>
    /// <param name="status">The final status (Completed or Abandoned). Defaults to Completed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ended session.</returns>
    /// <exception cref="InvalidOperationException">Thrown if session is not active.</exception>
    /// <exception cref="Exceptions.GraphOperationException">Thrown if session not found.</exception>
    Task<Session> EndSessionAsync(SessionStatus status = SessionStatus.Completed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a knowledge node to the graph.
    /// If resourceFolderPath points to a local folder, it will be zipped and uploaded to S3.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node within this session.</param>
    /// <param name="nodeType">Type of knowledge (e.g., MathAxiom, BiologyExperiment).</param>
    /// <param name="coreDescription">Core description - concise summary of the key conclusion.</param>
    /// <param name="detailedDescription">Detailed description - comprehensive explanation.</param>
    /// <param name="owner">Optional owner identifier.</param>
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
        string? owner = null,
        string? proof = null,
        string? resourceFolderPath = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts (create or update) a knowledge node.
    /// <para>
    /// - If the node does not exist, it will be created (empty core/detailed will fall back to nodeId).
    /// </para>
    /// <para>
    /// - If the node exists, non-empty inputs overwrite existing values; empty inputs keep existing values.
    /// </para>
    /// <para>
    /// This API is designed for iterative agent workflows where nodes are refined over time.
    /// </para>
    /// </summary>
    Task<KnowledgeNode> UpsertNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string? coreDescription = null,
        string? detailedDescription = null,
        string? owner = null,
        string? proof = null,
        string? resourceFolderPath = null,
        PivotNodeStatus? pivotStatus = null,
        DateTimeOffset? cancelledAt = null,
        string? cancelledByPivotId = null,
        string? directionContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds dependency edges for a node (node -[DEPENDS_ON]-> dependency).
    /// <para>This is idempotent: existing dependencies will be ignored.</para>
    /// </summary>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if the node or any dependency node does not exist.</exception>
    /// <exception cref="Exceptions.CycleDetectedException">Thrown if adding any dependency would create a cycle.</exception>
    Task AddDependenciesAsync(
        string nodeId,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete snapshot of the graph (all nodes with full information and edges within current session).
    /// </summary>
    Task<GraphSnapshot> GetGraphSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a complete snapshot of the graph (all nodes with full information and edges within current session).
    /// </summary>
    [Obsolete("Use GetGraphSnapshotAsync instead. This method will be removed in a future version.")]
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
    /// Returns the node as <see cref="IGraphNode"/> which can be either a <see cref="KnowledgeNode"/> or <see cref="PlanNode"/>.
    /// </summary>
    Task<IGraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a node from the graph.
    /// Also removes all edges connected to this node and deletes associated S3 files.
    /// </summary>
    Task<bool> RemoveNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    // ========== Plan Node Operations (FR-007) ==========

    /// <summary>
    /// Creates a PlanNode with the specified properties.
    /// PlanNodes represent research plan steps with status tracking.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node.</param>
    /// <param name="coreDescription">Core description - what this step aims to achieve.</param>
    /// <param name="detailedDescription">Detailed description of the plan step.</param>
    /// <param name="methodology">How this step will be executed.</param>
    /// <param name="sequentialOrder">Position in the plan sequence (1-based).</param>
    /// <param name="promotesNodeIds">Optional IDs of nodes this plan promotes (Promotes relationship).</param>
    /// <param name="dependsOnNodeIds">Optional IDs of nodes this plan depends on (DependsOn relationship).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created PlanNode.</returns>
    Task<PlanNode> CreatePlanNodeAsync(
        string nodeId,
        string coreDescription,
        string detailedDescription,
        string? methodology = null,
        int sequentialOrder = 0,
        IEnumerable<string>? promotesNodeIds = null,
        IEnumerable<string>? dependsOnNodeIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a PlanNode with optional progress text.
    /// Validates state transitions per FR-007:
    /// - Pending → Active (allowed)
    /// - Active → Completed (allowed)
    /// - Active → Pending (allowed, for re-planning)
    /// - Completed → any (not allowed)
    /// </summary>
    /// <param name="nodeId">The plan node ID to update.</param>
    /// <param name="newStatus">The new status.</param>
    /// <param name="progressText">Optional progress description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated PlanNode.</returns>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if node not found.</exception>
    /// <exception cref="Exceptions.GraphOperationException">Thrown if state transition is invalid.</exception>
    Task<PlanNode> UpdatePlanNodeStatusAsync(
        string nodeId,
        PlanNodeStatus newStatus,
        string? progressText = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all PlanNodes in the current session, ordered by SequentialOrder.
    /// </summary>
    Task<IReadOnlyList<PlanNode>> GetPlanNodesAsync(CancellationToken cancellationToken = default);

    // ========== Knowledge Node Operations (FR-008) ==========

    /// <summary>
    /// Creates a KnowledgeNode with full provenance tracking per FR-008.
    /// KnowledgeNodes represent asserted scientific knowledge with derivation process and references.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the node.</param>
    /// <param name="nodeType">Type of knowledge (MathAxiom, MathTheorem, etc.).</param>
    /// <param name="coreDescription">Core description - concise summary of the key conclusion.</param>
    /// <param name="detailedDescription">Comprehensive explanation of this knowledge.</param>
    /// <param name="derivationProcess">How this knowledge was derived (methodology, reasoning steps).</param>
    /// <param name="references">Source references (URLs, papers, citations) per RFC 3986.</param>
    /// <param name="proof">Optional proof in Markdown format.</param>
    /// <param name="motivatedByPlanNodeId">Optional plan node that motivated this knowledge (MotivatedBy relationship).</param>
    /// <param name="dependsOnNodeIds">Optional IDs of nodes this knowledge depends on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created KnowledgeNode.</returns>
    Task<KnowledgeNode> CreateKnowledgeNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        string? derivationProcess = null,
        IEnumerable<string>? references = null,
        string? proof = null,
        string? motivatedByPlanNodeId = null,
        IEnumerable<string>? dependsOnNodeIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all KnowledgeNodes in the current session.
    /// </summary>
    Task<IReadOnlyList<KnowledgeNode>> GetKnowledgeNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a KnowledgeNode to a PlanNode via MotivatedBy relationship.
    /// This indicates the knowledge was derived while executing the plan step.
    /// </summary>
    /// <param name="knowledgeNodeId">The knowledge node ID.</param>
    /// <param name="planNodeId">The plan node that motivated this knowledge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LinkKnowledgeToPlanAsync(
        string knowledgeNodeId,
        string planNodeId,
        CancellationToken cancellationToken = default);

    // ========== Node Explanation (US4) ==========

    /// <summary>
    /// Generates a detailed explanation of a node including its knowledge chain and relationships.
    /// For KnowledgeNodes: includes derivation process, references, upstream dependencies.
    /// For PlanNodes: includes status, methodology, and knowledge produced.
    /// </summary>
    /// <param name="nodeId">The node ID to explain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A detailed explanation with Markdown content and relationship information.</returns>
    /// <exception cref="Exceptions.NodeNotFoundException">Thrown if node not found.</exception>
    Task<NodeExplanation> ExplainNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default);

    // ========== Pivot Operations (US6) ==========

    /// <summary>
    /// Checks if a PlanNode can be safely deleted.
    /// A PlanNode can only be deleted if no KnowledgeNodes are linked to it via MotivatedBy.
    /// </summary>
    /// <param name="nodeId">The plan node ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the node can be deleted, false otherwise.</returns>
    Task<bool> CanDeletePlanNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets IDs of knowledge nodes that are motivated by the given plan node.
    /// Used to check if a plan node can be deleted.
    /// </summary>
    /// <param name="planNodeId">The plan node ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of knowledge node IDs motivated by this plan.</returns>
    Task<IReadOnlyList<string>> GetMotivatedKnowledgeNodeIdsAsync(string planNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a snapshot of the current graph state for pivot recovery.
    /// </summary>
    /// <param name="pivotReason">Description of why the pivot is being made.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created snapshot ID.</returns>
    Task<string> CreatePivotSnapshotAsync(string pivotReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pivot snapshots for the current session.
    /// </summary>
    Task<IReadOnlyList<PivotSnapshot>> GetPivotSnapshotsAsync(CancellationToken cancellationToken = default);

    // ========== Summary Generation (US7) ==========

    /// <summary>
    /// Generates a session summary showing plan progress and knowledge created.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A session summary with progress information.</returns>
    Task<Services.SessionSummary> GenerateSessionSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a full DAG summary showing all nodes and relationships.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DAG summary with structure analysis.</returns>
    Task<Services.DagSummary> GenerateFullDagSummaryAsync(CancellationToken cancellationToken = default);

    // ========== Global Migration Operations ==========

    /// <summary>
    /// Gets ALL knowledge nodes across all sessions (for migration purposes).
    /// </summary>
    Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesGlobalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ALL plan nodes across all sessions.
    /// </summary>
    Task<IReadOnlyList<PlanNode>> GetAllPlanNodesGlobalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ALL edges across all sessions (for migration purposes).
    /// </summary>
    Task<IReadOnlyList<(KnowledgeEdge Edge, string SessionId)>> GetAllEdgesGlobalAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating session-scoped knowledge graph clients.
/// </summary>
public interface IKnowledgeGraphClientFactory
{
    /// <summary>
    /// Creates a knowledge graph client for the specified session.
    /// Use this when you have an existing session ID.
    /// </summary>
    /// <param name="sessionId">The session ID for isolation.</param>
    /// <returns>A client scoped to the session.</returns>
    IKnowledgeGraphClient CreateClient(string sessionId);

    /// <summary>
    /// Starts a new session and returns a client scoped to it.
    /// This creates the session in storage and returns a ready-to-use client.
    /// </summary>
    /// <param name="sessionId">Optional session ID. If not provided, a new GUID-based ID is generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A client scoped to the newly created session.</returns>
    Task<IKnowledgeGraphClient> StartSessionAsync(string? sessionId = null, CancellationToken cancellationToken = default);
}
