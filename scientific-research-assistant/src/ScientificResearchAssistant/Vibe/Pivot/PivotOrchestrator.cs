using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Vibe.Pivot.Models;

namespace ScientificResearchAssistant.Vibe.Pivot;

/// <summary>
/// Orchestrates research direction pivot operations on the knowledge graph.
/// Handles DAG updates including soft-delete, node preservation, and new node creation.
/// </summary>
public sealed class PivotOrchestrator : IPivotOrchestrator
{
    private readonly IKnowledgeGraphClientFactory _clientFactory;
    private readonly IPivotSnapshotManager _snapshotManager;
    private readonly PivotOptions _options;
    private readonly ILogger<PivotOrchestrator> _logger;

    public PivotOrchestrator(
        IKnowledgeGraphClientFactory clientFactory,
        IPivotSnapshotManager snapshotManager,
        IOptions<PivotOptions> options,
        ILogger<PivotOrchestrator> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        _options = options?.Value ?? new PivotOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PivotOperation> ExecutePivotAsync(
        DirectionChangeIntent intent,
        string? oldDirection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!intent.IsDirectionChange)
        {
            throw new ArgumentException("Intent does not indicate a direction change", nameof(intent));
        }

        var operation = PivotOperation.Create(intent);
        operation.OldDirection = oldDirection;
        operation.NewDirection = intent.NewTopic;

        _logger.LogInformation(
            "Starting pivot operation {PivotId} for session {SessionId}: {OldDirection} -> {NewDirection}",
            operation.PivotId, operation.SessionId, oldDirection ?? "(none)", intent.NewTopic ?? "(new)");

        var client = _clientFactory.CreateClient(intent.SessionId);

        try
        {
            // Step 0: Create rollback snapshot before any modifications (T7.2)
            var directionSummary = $"{oldDirection ?? "unknown"} → {intent.NewTopic ?? "new direction"}";
            var snapshotMetadata = await _snapshotManager.CreateSnapshotAsync(
                intent.SessionId,
                operation.PivotId,
                directionSummary,
                cancellationToken);
            operation.SnapshotId = snapshotMetadata.SnapshotId;

            // Reuse the snapshot from metadata (avoid duplicate GetKnowledgeSnapshotAsync call)
            var snapshot = snapshotMetadata.Snapshot;

            _logger.LogDebug(
                "Pivot {PivotId}: Using snapshot {SnapshotId} with {NodeCount} nodes, expires at {ExpiresAt}",
                operation.PivotId, snapshotMetadata.SnapshotId, snapshot.NodeCount, snapshotMetadata.ExpiresAt);

            // Step 2: Identify nodes to cancel, preserve, or mark as superseded
            var (cancelled, preserved, superseded) = await ClassifyNodesForPivotAsync(
                client, snapshot, intent, operation.PivotId, cancellationToken);

            operation.CancelledNodeIds.AddRange(cancelled);
            operation.PreservedNodeIds.AddRange(preserved);
            operation.Status = PivotStatus.UpdatingDAG;

            _logger.LogInformation(
                "Pivot {PivotId}: Classified nodes - Cancelled={Cancelled}, Preserved={Preserved}, Superseded={Superseded}",
                operation.PivotId, cancelled.Count, preserved.Count, superseded.Count);

            // Step 3: Update nodes in the graph
            await UpdateNodesForPivotAsync(
                client, snapshot, cancelled, superseded, operation.PivotId, intent, cancellationToken);

            // Mark operation as completed
            operation.Complete(PivotStatus.Completed);

            _logger.LogInformation(
                "Pivot operation {PivotId} completed successfully in {DurationMs}ms",
                operation.PivotId, operation.DurationMs);

            return operation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pivot operation {PivotId} failed", operation.PivotId);
            operation.Complete(PivotStatus.Failed, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<string> Cancelled, IReadOnlyList<string> Preserved, IReadOnlyList<string> Superseded)>
        ClassifyNodesForPivotAsync(
            IKnowledgeGraphClient client,
            KnowledgeSnapshot snapshot,
            DirectionChangeIntent intent,
            string pivotId,
            CancellationToken cancellationToken = default)
    {
        var cancelled = new List<string>();
        var preserved = new List<string>();
        var superseded = new List<string>();

        foreach (var node in snapshot.Nodes)
        {
            // Skip already cancelled nodes
            if (node.PivotStatus == PivotNodeStatus.Cancelled)
            {
                continue;
            }

            // Check if node should be preserved based on PreserveAspects
            if (ShouldPreserveNode(node, intent.PreserveAspects))
            {
                preserved.Add(node.Id);
                continue;
            }

            // Classify based on node kind
            if (node.Kind == KnowledgeNodeKind.Plan && node.PivotStatus == PivotNodeStatus.Active)
            {
                // Pending plan nodes should be cancelled
                cancelled.Add(node.Id);
            }
            else if (node.Kind == KnowledgeNodeKind.Knowledge && node.PivotStatus == PivotNodeStatus.Active)
            {
                // Completed knowledge nodes should be marked as superseded
                superseded.Add(node.Id);
            }
        }

        return (cancelled, preserved, superseded);
    }

    /// <inheritdoc />
    public bool ShouldPreserveNode(KnowledgeNode node, IReadOnlyList<string> preserveAspects)
    {
        if (preserveAspects.Count == 0)
        {
            return false;
        }

        foreach (var aspect in preserveAspects)
        {
            var normalizedAspect = aspect.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedAspect))
            {
                continue;
            }

            // Check 1: Exact keyword match on DirectionContext
            if (!string.IsNullOrEmpty(node.DirectionContext) &&
                node.DirectionContext.ToLowerInvariant().Contains(normalizedAspect))
            {
                return true;
            }

            // Check 2: Keyword match on CoreDescription
            if (!string.IsNullOrEmpty(node.CoreDescription) &&
                node.CoreDescription.ToLowerInvariant().Contains(normalizedAspect))
            {
                return true;
            }

            // Check 3: Keyword match on DetailedDescription
            if (!string.IsNullOrEmpty(node.DetailedDescription) &&
                node.DetailedDescription.ToLowerInvariant().Contains(normalizedAspect))
            {
                return true;
            }
        }

        return false;
    }

    private async Task UpdateNodesForPivotAsync(
        IKnowledgeGraphClient client,
        KnowledgeSnapshot snapshot,
        IReadOnlyList<string> cancelledIds,
        IReadOnlyList<string> supersededIds,
        string pivotId,
        DirectionChangeIntent intent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Cancel pending plan nodes
        foreach (var nodeId in cancelledIds)
        {
            var node = snapshot.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                continue;
            }

            await client.UpsertNodeAsync(
                nodeId,
                node.NodeType,
                pivotStatus: PivotNodeStatus.Cancelled,
                cancelledAt: now,
                cancelledByPivotId: pivotId,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Pivot {PivotId}: Cancelled node {NodeId}", pivotId, nodeId);
        }

        // Mark completed nodes as superseded
        foreach (var nodeId in supersededIds)
        {
            var node = snapshot.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                continue;
            }

            await client.UpsertNodeAsync(
                nodeId,
                node.NodeType,
                pivotStatus: PivotNodeStatus.Superseded,
                directionContext: node.DirectionContext ?? intent.NewTopic,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Pivot {PivotId}: Marked node {NodeId} as superseded", pivotId, nodeId);
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeNode> CreatePlanNodeAsync(
        string sessionId,
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        string? directionContext = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        var client = _clientFactory.CreateClient(sessionId);

        var node = await client.AddNodeAsync(
            nodeId,
            nodeType,
            coreDescription,
            detailedDescription,
            kind: KnowledgeNodeKind.Plan,
            dependsOn: dependsOn,
            cancellationToken: cancellationToken);

        // Update with pivot-specific fields
        if (!string.IsNullOrEmpty(directionContext))
        {
            node = await client.UpsertNodeAsync(
                nodeId,
                nodeType,
                directionContext: directionContext,
                cancellationToken: cancellationToken);
        }

        _logger.LogDebug(
            "Created plan node {NodeId} for session {SessionId} with direction: {Direction}",
            nodeId, sessionId, directionContext ?? "(none)");

        return node;
    }
}
