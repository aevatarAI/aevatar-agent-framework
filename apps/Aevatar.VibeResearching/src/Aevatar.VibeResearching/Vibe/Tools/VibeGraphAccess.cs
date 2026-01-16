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

    // ========== Helpers ==========

    private static Struct ToStruct(object obj) => JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(obj));
}
