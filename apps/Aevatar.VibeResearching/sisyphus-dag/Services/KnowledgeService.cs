namespace SisyphusDag.Services;

using SisyphusDag.Data;
using SisyphusDag.Dtos;
using SisyphusDag.Models;

/// <summary>
/// Business logic implementation for knowledge DAG operations.
/// Handles ID generation, timestamps, audit fields, and snapshot assembly.
/// </summary>
public sealed class KnowledgeService : IKnowledgeService
{
    private const string DefaultActor = "system";

    private readonly IKnowledgeRepository _repository;

    public KnowledgeService(IKnowledgeRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<List<string>> CreateAsync(
        KnowledgesDto request,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        var ids = new List<string>(request.KnowledgeList.Count);
        var nodeParams = new List<Dictionary<string, object?>>(request.KnowledgeList.Count);

        foreach (var dto in request.KnowledgeList)
        {
            var id = Guid.NewGuid().ToString();
            ids.Add(id);
            nodeParams.Add(BuildNodeParams(id, dto, now));
        }

        await _repository.CreateNodesAsync(nodeParams, ct);

        var edgeParams = CollectEdgeParams(request, ids, now);
        if (edgeParams.Count > 0)
            await _repository.CreateEdgesAsync(edgeParams, ct);

        return ids;
    }

    /// <inheritdoc />
    public async Task<KnowledgeSnapshotDto> GetSnapshotAsync(CancellationToken ct = default)
    {
        var nodesTask = _repository.GetAllNodesAsync(ct);
        var edgesTask = _repository.GetAllEdgesAsync(ct);
        await Task.WhenAll(nodesTask, edgesTask);

        var nodes = nodesTask.Result;
        var edges = edgesTask.Result;

        return BuildSnapshot(nodes, edges);
    }

    /// <inheritdoc />
    public async Task<List<string>> UpdateAsync(
        Dictionary<string, KnowledgeDto> updates,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.ToString("o");
        var updateParams = new List<Dictionary<string, object?>>(updates.Count);

        foreach (var (id, dto) in updates)
            updateParams.Add(BuildUpdateParams(id, dto, now));

        await _repository.UpdateNodesAsync(updateParams, ct);

        return updates.Keys.ToList();
    }

    /// <inheritdoc />
    public async Task<List<string>> DeleteAsync(
        List<string> ids,
        CancellationToken ct = default)
    {
        await _repository.DeleteNodesAsync(ids, ct);
        return ids;
    }

    /// <inheritdoc />
    public async Task<string> ExplainAsync(string id, int level = 10, CancellationToken ct = default)
    {
        var node = await _repository.GetNodeByIdAsync(id, ct);
        if (node is null)
            throw new KeyNotFoundException($"Knowledge node '{id}' not found.");

        var parentIdsTask = _repository.GetParentIdsAsync(id, ct);
        var childIdsTask = _repository.GetChildIdsAsync(id, ct);
        await Task.WhenAll(parentIdsTask, childIdsTask);

        var parentNodesTask = _repository.GetNodesByIdsAsync(parentIdsTask.Result, ct);
        var childNodesTask = _repository.GetNodesByIdsAsync(childIdsTask.Result, ct);
        await Task.WhenAll(parentNodesTask, childNodesTask);

        var maxLevel = Math.Clamp(level, 1, 50);
        var upstreamChain = await BuildChainAsync(
            id, parentIdsTask.Result, _repository.GetParentIdsAsync, maxLevel, ct);
        var downstreamChain = await BuildChainAsync(
            id, childIdsTask.Result, _repository.GetChildIdsAsync, maxLevel, ct);

        return ExplainMarkdownBuilder.Build(
            node, parentNodesTask.Result, childNodesTask.Result,
            upstreamChain, downstreamChain, maxLevel);
    }

    private async Task<List<(int Level, List<KnowledgeNode> Nodes)>> BuildChainAsync(
        string rootId,
        List<string> initialIds,
        Func<string, CancellationToken, Task<List<string>>> getNextIds,
        int maxLevel,
        CancellationToken ct)
    {
        var result = new List<(int Level, List<KnowledgeNode> Nodes)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var currentIds = initialIds;

        for (var level = 1; level <= maxLevel; level++)
        {
            var unvisitedIds = currentIds.Where(id => !visited.Contains(id)).Distinct().ToList();
            if (unvisitedIds.Count == 0)
                break;

            var nodes = await _repository.GetNodesByIdsAsync(unvisitedIds, ct);
            foreach (var id in unvisitedIds)
                visited.Add(id);

            if (nodes.Count > 0)
                result.Add((level, nodes));

            var nextIdTasks = nodes.Select(n => getNextIds(n.Id, ct));
            var nextIdResults = await Task.WhenAll(nextIdTasks);
            currentIds = nextIdResults.SelectMany(ids => ids).ToList();
        }

        return result;
    }

    private static Dictionary<string, object?> BuildNodeParams(
        string id,
        KnowledgeDto dto,
        string now)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["title"] = dto.Title,
            ["sessionId"] = dto.SessionId ?? string.Empty,
            ["description"] = dto.Description,
            ["deriveDetails"] = dto.DeriveDetail,
            ["resourceUri"] = dto.ResourceUri ?? string.Empty,
            ["references"] = dto.References ?? [],
            ["lastReviewedAt"] = string.Empty,
            ["isActive"] = true,
            ["deactivateReason"] = string.Empty,
            ["deactivateAt"] = string.Empty,
            ["createdBy"] = DefaultActor,
            ["createdAt"] = now,
            ["updatedBy"] = DefaultActor,
            ["updatedAt"] = now,
        };
    }

    private static Dictionary<string, object?> BuildUpdateParams(
        string id,
        KnowledgeDto dto,
        string now)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["title"] = dto.Title,
            ["sessionId"] = dto.SessionId ?? string.Empty,
            ["description"] = dto.Description,
            ["deriveDetails"] = dto.DeriveDetail,
            ["resourceUri"] = dto.ResourceUri ?? string.Empty,
            ["references"] = dto.References ?? [],
            ["updatedBy"] = DefaultActor,
            ["updatedAt"] = now,
        };
    }

    private static List<Dictionary<string, object?>> CollectEdgeParams(
        KnowledgesDto request,
        List<string> ids,
        string now)
    {
        var edgeParams = new List<Dictionary<string, object?>>();

        // Edges from DependsOn lists (new node -> existing node)
        for (var i = 0; i < request.KnowledgeList.Count; i++)
        {
            var dependsOn = request.KnowledgeList[i].DependsOn;
            if (dependsOn is null) continue;

            foreach (var targetId in dependsOn)
                edgeParams.Add(BuildEdgeParams(ids[i], targetId, now));
        }

        // Edges from IndexDependencies (parent index -> child indices)
        if (request.IndexDependencies is null) return edgeParams;

        foreach (var (parentIdx, childIndices) in request.IndexDependencies)
        {
            foreach (var childIdx in childIndices)
                edgeParams.Add(BuildEdgeParams(ids[parentIdx], ids[childIdx], now));
        }

        return edgeParams;
    }

    private static Dictionary<string, object?> BuildEdgeParams(
        string fromId,
        string toId,
        string now)
    {
        return new Dictionary<string, object?>
        {
            ["fromId"] = fromId,
            ["toId"] = toId,
            ["createdBy"] = DefaultActor,
            ["createdAt"] = now,
            ["updatedBy"] = DefaultActor,
            ["updatedAt"] = now,
        };
    }

    private static KnowledgeSnapshotDto BuildSnapshot(
        List<KnowledgeNode> nodes,
        List<KnowledgeDependsOnEdge> edges)
    {
        var nodeMap = new Dictionary<string, KnowledgeNode>(nodes.Count);
        var parentsMap = new Dictionary<string, List<string>>(nodes.Count);
        var childrenMap = new Dictionary<string, List<string>>(nodes.Count);

        // Initialize maps with empty lists for every node
        foreach (var node in nodes)
        {
            nodeMap[node.Id] = node;
            parentsMap[node.Id] = [];
            childrenMap[node.Id] = [];
        }

        // Populate parent/children maps from edges
        // Edge convention: (from)-[:DEPENDS_ON]->(to), from=child, to=parent
        foreach (var edge in edges)
        {
            if (parentsMap.TryGetValue(edge.FromId, out var parents))
                parents.Add(edge.ToId);

            if (childrenMap.TryGetValue(edge.ToId, out var children))
                children.Add(edge.FromId);
        }

        return new KnowledgeSnapshotDto
        {
            SnapshotTakenAt = DateTime.UtcNow.ToString("o"),
            Nodes = nodes,
            Edges = edges,
            NodeMap = nodeMap,
            ParentsMap = parentsMap,
            ChildrenMap = childrenMap,
        };
    }
}
