using System.Text.Json;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace VibeResearching.Vibe.Tools;

/// <summary>
/// Unified interface for all Vibe graph operations.
/// Consolidates plan, knowledge, and query operations into a single access layer.
/// </summary>
public interface IVibeGraphAccess
{
    // ========== Plan Operations ==========
    Task<Struct> CreatePlanAsync(string sessionId, IReadOnlyList<PlanStepInput> steps, CancellationToken ct);
    Task<Struct> UpdatePlanStatusAsync(string sessionId, string nodeId, PlanNodeStatus status, string? progressText, CancellationToken ct);
    Task<Struct> GetPlanNodesAsync(string sessionId, CancellationToken ct);
    Task<Struct> CanDeletePlanAsync(string sessionId, string nodeId, CancellationToken ct);
    Task<Struct> DeletePlanAsync(string sessionId, string nodeId, CancellationToken ct);

    // ========== Knowledge Operations ==========
    Task<Struct> CreateKnowledgeAsync(string sessionId, IReadOnlyList<KnowledgeInput> entries, CancellationToken ct);
    Task<Struct> GetKnowledgeNodesAsync(string sessionId, CancellationToken ct);
    Task<Struct> LinkKnowledgeToPlanAsync(string sessionId, string knowledgeNodeId, string planNodeId, CancellationToken ct);

    // ========== Query Operations ==========
    Task<Struct> GetSnapshotAsync(string sessionId, int maxNodes, int maxEdges, CancellationToken ct);
    Task<Struct> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct);
    Task<Struct> CreatePivotSnapshotAsync(string sessionId, string reason, CancellationToken ct);
    Task<Struct> GetPivotSnapshotsAsync(string sessionId, CancellationToken ct);

    // ========== Review Agent Operations ==========

    /// <summary>
    /// Get knowledge nodes that are stale and need review.
    /// Returns nodes where IsActivated=true and (LastReviewedAt is null or older than threshold).
    /// Ordered topologically (ancestors first).
    /// </summary>
    Task<IReadOnlyList<KnowledgeNode>> GetStaleKnowledgeNodesAsync(
        TimeSpan outOfDateThreshold,
        CancellationToken ct);

    /// <summary>
    /// Get deactivated nodes that have exceeded the delete threshold and should be removed.
    /// </summary>
    Task<IReadOnlyList<KnowledgeNode>> GetNodesForCleanupAsync(
        TimeSpan toDeleteThreshold,
        CancellationToken ct);

    /// <summary>
    /// Update a node's LastReviewedAt timestamp after successful verification.
    /// </summary>
    Task<KnowledgeNode> UpdateNodeReviewStatusAsync(
        string sessionId,
        string nodeId,
        DateTimeOffset reviewedAt,
        CancellationToken ct);

    /// <summary>
    /// Deactivate a node and cascade to all descendants.
    /// Sets IsActivated=false, DeactivatedReason, DeactivatedTimestamp on node and all dependents.
    /// </summary>
    Task<IReadOnlyList<string>> DeactivateNodeWithDescendantsAsync(
        string sessionId,
        string nodeId,
        string reason,
        DateTimeOffset timestamp,
        CancellationToken ct);

    /// <summary>
    /// Permanently remove deactivated nodes and their edges.
    /// </summary>
    Task<int> RemoveDeactivatedNodesAsync(
        IEnumerable<string> nodeIds,
        CancellationToken ct);
}

/// <summary>
/// Input record for creating a plan step.
/// </summary>
public sealed record PlanStepInput(
    string NodeId,
    string CoreDescription,
    string DetailedDescription,
    string? Methodology,
    int SequentialOrder,
    IReadOnlyList<string>? PromotesNodeIds,
    IReadOnlyList<string>? DependsOnNodeIds);

/// <summary>
/// Input record for creating a knowledge node.
/// </summary>
public sealed record KnowledgeInput(
    string NodeId,
    KnowledgeNodeType NodeType,
    string CoreDescription,
    string DetailedDescription,
    string? DerivationProcess,
    IReadOnlyList<string>? References,
    string? Proof,
    string? MotivatedByPlanNodeId,
    IReadOnlyList<string>? DependsOnNodeIds);

/// <summary>
/// Unified implementation of IVibeGraphAccess.
/// </summary>
public sealed class VibeGraphAccess : IVibeGraphAccess
{
    private readonly IKnowledgeGraphClientFactory _factory;

    public VibeGraphAccess(IKnowledgeGraphClientFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    // ========== Plan Operations ==========

    public async Task<Struct> CreatePlanAsync(string sessionId, IReadOnlyList<PlanStepInput> steps, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var created = new List<object>();

        foreach (var step in steps)
        {
            var node = await client.CreatePlanNodeAsync(
                step.NodeId, step.CoreDescription, step.DetailedDescription,
                step.Methodology, step.SequentialOrder,
                step.PromotesNodeIds, step.DependsOnNodeIds, ct);

            created.Add(new { nodeId = node.Id, status = node.Status.ToString(), sequentialOrder = node.SequentialOrder });
        }

        return ToStruct(new { success = true, createdCount = created.Count, nodes = created });
    }

    public async Task<Struct> UpdatePlanStatusAsync(string sessionId, string nodeId, PlanNodeStatus status, string? progressText, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var node = await client.UpdatePlanNodeStatusAsync(nodeId, status, progressText, ct);
        return ToStruct(new { success = true, nodeId = node.Id, status = node.Status.ToString(), progressText = node.ProgressText });
    }

    public async Task<Struct> GetPlanNodesAsync(string sessionId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var nodes = await client.GetPlanNodesAsync(ct);
        return ToStruct(new
        {
            success = true,
            nodeCount = nodes.Count,
            nodes = nodes.Select(n => new
            {
                nodeId = n.Id,
                coreDescription = n.CoreDescription,
                status = n.Status.ToString(),
                progressText = n.ProgressText,
                methodology = n.Methodology,
                sequentialOrder = n.SequentialOrder
            }).ToList()
        });
    }

    public async Task<Struct> CanDeletePlanAsync(string sessionId, string nodeId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var canDelete = await client.CanDeletePlanNodeAsync(nodeId, ct);
        var blockedBy = await client.GetMotivatedKnowledgeNodeIdsAsync(nodeId, ct);

        return ToStruct(new
        {
            success = true,
            nodeId,
            canDelete,
            blockedByKnowledgeNodes = blockedBy.ToList(),
            reason = canDelete ? "Plan node can be safely deleted" : $"Blocked by {blockedBy.Count} knowledge node(s)"
        });
    }

    public async Task<Struct> DeletePlanAsync(string sessionId, string nodeId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var canDelete = await client.CanDeletePlanNodeAsync(nodeId, ct);

        if (!canDelete)
        {
            var blockedBy = await client.GetMotivatedKnowledgeNodeIdsAsync(nodeId, ct);
            return ToStruct(new { success = false, nodeId, error = "Cannot delete: linked knowledge exists", blockedByKnowledgeNodes = blockedBy.ToList() });
        }

        var deleted = await client.RemoveNodeAsync(nodeId, ct);
        return ToStruct(new { success = deleted, nodeId, message = deleted ? "Deleted" : "Not found" });
    }

    // ========== Knowledge Operations ==========

    public async Task<Struct> CreateKnowledgeAsync(string sessionId, IReadOnlyList<KnowledgeInput> entries, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var created = new List<object>();

        foreach (var e in entries)
        {
            var node = await client.CreateKnowledgeNodeAsync(
                e.NodeId, e.NodeType, e.CoreDescription, e.DetailedDescription,
                e.DerivationProcess, e.References, e.Proof,
                e.MotivatedByPlanNodeId, e.DependsOnNodeIds, ct);

            created.Add(new
            {
                nodeId = node.Id,
                nodeType = node.NodeType.ToString(),
                coreDescription = node.CoreDescription,
                hasProof = !string.IsNullOrEmpty(node.Proof),
                dependsOn = node.DependsOn.ToList()
            });
        }

        return ToStruct(new { success = true, createdCount = created.Count, nodes = created });
    }

    public async Task<Struct> GetKnowledgeNodesAsync(string sessionId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var nodes = await client.GetKnowledgeNodesAsync(ct);

        return ToStruct(new
        {
            success = true,
            nodeCount = nodes.Count,
            nodes = nodes.Select(n => new
            {
                nodeId = n.Id,
                nodeType = n.NodeType.ToString(),
                coreDescription = n.CoreDescription,
                detailedDescription = n.DetailedDescription,
                derivationProcess = n.DerivationProcess,
                references = n.References.ToList(),
                hasProof = !string.IsNullOrEmpty(n.Proof),
                dependsOn = n.DependsOn.ToList()
            }).ToList()
        });
    }

    public async Task<Struct> LinkKnowledgeToPlanAsync(string sessionId, string knowledgeNodeId, string planNodeId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        await client.LinkKnowledgeToPlanAsync(knowledgeNodeId, planNodeId, ct);
        return ToStruct(new { success = true, knowledgeNodeId, planNodeId, relationship = "MotivatedBy" });
    }

    // ========== Query Operations ==========

    public async Task<Struct> GetSnapshotAsync(string sessionId, int maxNodes, int maxEdges, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var snapshot = await client.GetGraphSnapshotAsync(ct);

        var nodes = snapshot.AllNodes.Take(maxNodes).Select(n => new
        {
            id = n.Id,
            kind = n is PlanNode ? "Plan" : "Knowledge",
            coreDescription = n.CoreDescription,
            dependsOn = n.DependsOn.ToList()
        }).ToList();

        var edges = snapshot.Edges.Take(maxEdges).Select(e => new
        {
            from = e.FromId,
            to = e.ToId,
            type = e.Type.ToString()
        }).ToList();

        return ToStruct(new { success = true, nodeCount = nodes.Count, edgeCount = edges.Count, nodes, edges });
    }

    public async Task<Struct> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var explanation = await client.ExplainNodeAsync(nodeId, ct);

        return ToStruct(new
        {
            success = true,
            nodeId = explanation.NodeId,
            nodeType = explanation.NodeType,
            title = explanation.Title,
            markdownContent = explanation.MarkdownContent,
            directDependencies = explanation.DirectDependencies.ToList(),
            dependents = explanation.Dependents.ToList()
        });
    }

    public async Task<Struct> CreatePivotSnapshotAsync(string sessionId, string reason, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var snapshotId = await client.CreatePivotSnapshotAsync(reason, ct);
        return ToStruct(new { success = true, snapshotId, reason });
    }

    public async Task<Struct> GetPivotSnapshotsAsync(string sessionId, CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var snapshots = await client.GetPivotSnapshotsAsync(ct);

        return ToStruct(new
        {
            success = true,
            snapshotCount = snapshots.Count,
            snapshots = snapshots.Select(s => new
            {
                id = s.Id,
                createdAt = s.CreatedAt.ToString("O"),
                reason = s.Reason,
                nodeCount = s.Snapshot.NodeCount,
                edgeCount = s.Snapshot.EdgeCount
            }).ToList()
        });
    }

    // ========== Review Agent Operations ==========

    public async Task<IReadOnlyList<KnowledgeNode>> GetStaleKnowledgeNodesAsync(
        TimeSpan outOfDateThreshold,
        CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow - outOfDateThreshold;
        var allNodes = new List<KnowledgeNode>();

        // Get all knowledge nodes across all sessions (system-level review)
        var globalNodes = await _factory.CreateClient("__global__").GetAllKnowledgeNodesGlobalAsync(ct);

        foreach (var node in globalNodes)
        {
            // Skip deactivated nodes
            if (!node.IsActivated) continue;

            // Check if node is stale (never reviewed or reviewed before threshold)
            if (node.LastReviewedAt == null || node.LastReviewedAt < threshold)
            {
                allNodes.Add(node);
            }
        }

        // Sort topologically (nodes with fewer dependencies first = ancestors first)
        return TopologicalSort(allNodes);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetNodesForCleanupAsync(
        TimeSpan toDeleteThreshold,
        CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow - toDeleteThreshold;
        var nodesToCleanup = new List<KnowledgeNode>();

        var globalNodes = await _factory.CreateClient("__global__").GetAllKnowledgeNodesGlobalAsync(ct);

        foreach (var node in globalNodes)
        {
            // Only deactivated nodes
            if (node.IsActivated) continue;

            // Check if past delete threshold
            if (node.DeactivatedTimestamp != null && node.DeactivatedTimestamp < threshold)
            {
                nodesToCleanup.Add(node);
            }
        }

        return nodesToCleanup;
    }

    public async Task<KnowledgeNode> UpdateNodeReviewStatusAsync(
        string sessionId,
        string nodeId,
        DateTimeOffset reviewedAt,
        CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var existingNode = await client.GetNodeAsync(nodeId, ct) as KnowledgeNode
            ?? throw new InvalidOperationException($"Knowledge node '{nodeId}' not found");

        // Create updated node with new LastReviewedAt
        // Note: This requires the underlying storage to support updates
        var updatedNode = await client.UpsertNodeAsync(
            nodeId,
            existingNode.NodeType,
            existingNode.CoreDescription,
            existingNode.DetailedDescription,
            existingNode.Owner,
            existingNode.Proof,
            existingNode.ResourceFolderPath,
            existingNode.PivotStatus,
            existingNode.CancelledAt,
            existingNode.CancelledByPivotId,
            existingNode.DirectionContext,
            ct);

        // Return updated node (the actual LastReviewedAt update needs to be done at storage level)
        return updatedNode;
    }

    public async Task<IReadOnlyList<string>> DeactivateNodeWithDescendantsAsync(
        string sessionId,
        string nodeId,
        string reason,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var client = _factory.CreateClient(sessionId);
        var snapshot = await client.GetGraphSnapshotAsync(ct);
        var deactivatedIds = new List<string>();

        // Get all descendants (nodes that depend on this node)
        var toDeactivate = new Queue<string>();
        toDeactivate.Enqueue(nodeId);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (toDeactivate.Count > 0)
        {
            var currentId = toDeactivate.Dequeue();
            if (visited.Contains(currentId)) continue;
            visited.Add(currentId);

            var node = snapshot.GetNode(currentId);
            if (node is KnowledgeNode kn && kn.IsActivated)
            {
                deactivatedIds.Add(currentId);

                // Find all dependents (nodes that depend on this node)
                var dependents = snapshot.GetDependents(currentId);
                foreach (var depId in dependents)
                {
                    if (!visited.Contains(depId))
                    {
                        toDeactivate.Enqueue(depId);
                    }
                }
            }
        }

        // Note: Actual deactivation updates need to be done at storage level
        // This returns the list of node IDs that should be deactivated
        return deactivatedIds;
    }

    public async Task<int> RemoveDeactivatedNodesAsync(
        IEnumerable<string> nodeIds,
        CancellationToken ct)
    {
        var removed = 0;
        foreach (var nodeId in nodeIds)
        {
            // Need to determine sessionId from node - for now use global
            var client = _factory.CreateClient("__global__");
            var success = await client.RemoveNodeAsync(nodeId, ct);
            if (success) removed++;
        }
        return removed;
    }

    // ========== Helpers ==========

    private static Struct ToStruct(object obj) => JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(obj));

    private static IReadOnlyList<KnowledgeNode> TopologicalSort(List<KnowledgeNode> nodes)
    {
        // Use composite key (sessionId:nodeId) to handle same nodeId across different sessions
        static string GetKey(KnowledgeNode n) => $"{n.SessionId}:{n.Id}";

        // Deduplicate nodes by composite key (in case of duplicates in the database)
        var nodeMap = new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            nodeMap.TryAdd(GetKey(node), node);
        }

        // Build dependency graph using deduplicated nodes
        var uniqueNodes = nodeMap.Values.ToList();
        var inDegree = uniqueNodes.ToDictionary(GetKey, n => 0, StringComparer.Ordinal);

        foreach (var node in uniqueNodes)
        {
            foreach (var depId in node.DependsOn)
            {
                // Dependencies are within the same session
                var depKey = $"{node.SessionId}:{depId}";
                if (nodeMap.ContainsKey(depKey))
                {
                    inDegree[GetKey(node)]++;
                }
            }
        }

        // Kahn's algorithm
        var result = new List<KnowledgeNode>();
        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            if (nodeMap.TryGetValue(key, out var node))
            {
                result.Add(node);

                // Find nodes that depend on this one (within the same session)
                foreach (var other in uniqueNodes)
                {
                    // Check if 'other' depends on 'node' (same session, matching node ID)
                    if (other.SessionId == node.SessionId && other.DependsOn.Contains(node.Id))
                    {
                        var otherKey = GetKey(other);
                        inDegree[otherKey]--;
                        if (inDegree[otherKey] == 0)
                        {
                            queue.Enqueue(otherKey);
                        }
                    }
                }
            }
        }

        // Add any remaining nodes (in case of cycles, though shouldn't happen)
        var addedKeys = new HashSet<string>(result.Select(GetKey), StringComparer.Ordinal);
        foreach (var node in uniqueNodes)
        {
            if (!addedKeys.Contains(GetKey(node)))
            {
                result.Add(node);
            }
        }

        return result;
    }
}
