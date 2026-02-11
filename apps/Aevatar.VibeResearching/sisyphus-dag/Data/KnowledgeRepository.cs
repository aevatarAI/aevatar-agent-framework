namespace SisyphusDag.Data;

using Aevatar.Agents.Persistence.Neo4j;
using Neo4j.Driver;
using SisyphusDag.Models;

/// <summary>
/// Cypher-based Neo4j repository for knowledge nodes and edges.
/// All batch operations use UNWIND for single-statement atomicity.
/// </summary>
public sealed class KnowledgeRepository : IKnowledgeRepository
{
    private readonly INeo4jClient _client;

    private const string CreateNodesCypher = """
        UNWIND $nodes AS n
        CREATE (k:KnowledgeNode {
          id: n.id,
          title: n.title,
          sessionId: n.sessionId,
          description: n.description,
          deriveDetails: n.deriveDetails,
          resourceUri: n.resourceUri,
          references: n.references,
          lastReviewedAt: n.lastReviewedAt,
          isActive: n.isActive,
          deactivateReason: n.deactivateReason,
          deactivateAt: n.deactivateAt,
          createdBy: n.createdBy,
          createdAt: n.createdAt,
          updatedBy: n.updatedBy,
          updatedAt: n.updatedAt
        })
        """;

    private const string CreateEdgesCypher = """
        UNWIND $edges AS e
        MATCH (from:KnowledgeNode {id: e.fromId})
        MATCH (to:KnowledgeNode {id: e.toId})
        CREATE (from)-[:DEPENDS_ON {
          createdBy: e.createdBy,
          createdAt: e.createdAt,
          updatedBy: e.updatedBy,
          updatedAt: e.updatedAt
        }]->(to)
        """;

    private const string GetAllNodesCypher = """
        MATCH (k:KnowledgeNode)
        RETURN k
        """;

    private const string GetAllEdgesCypher = """
        MATCH (from:KnowledgeNode)-[r:DEPENDS_ON]->(to:KnowledgeNode)
        RETURN from.id AS fromId, to.id AS toId,
               r.createdBy AS createdBy, r.createdAt AS createdAt,
               r.updatedBy AS updatedBy, r.updatedAt AS updatedAt
        """;

    private const string UpdateNodesCypher = """
        UNWIND $updates AS u
        MATCH (k:KnowledgeNode {id: u.id})
        SET k.title = u.title,
            k.sessionId = u.sessionId,
            k.description = u.description,
            k.deriveDetails = u.deriveDetails,
            k.resourceUri = u.resourceUri,
            k.references = u.references,
            k.updatedBy = u.updatedBy,
            k.updatedAt = u.updatedAt
        """;

    private const string DeleteNodesCypher = """
        UNWIND $ids AS nodeId
        MATCH (k:KnowledgeNode {id: nodeId})
        DETACH DELETE k
        """;

    private const string GetNodeByIdCypher = """
        MATCH (k:KnowledgeNode {id: $id})
        RETURN k
        """;

    private const string GetParentIdsCypher = """
        MATCH (k:KnowledgeNode {id: $id})-[:DEPENDS_ON]->(parent:KnowledgeNode)
        RETURN parent.id AS parentId
        """;

    private const string GetChildIdsCypher = """
        MATCH (child:KnowledgeNode)-[:DEPENDS_ON]->(k:KnowledgeNode {id: $id})
        RETURN child.id AS childId
        """;

    private const string GetNodesByIdsCypher = """
        UNWIND $ids AS nodeId
        MATCH (k:KnowledgeNode {id: nodeId})
        RETURN k
        """;

    public KnowledgeRepository(INeo4jClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task CreateNodesAsync(
        List<Dictionary<string, object?>> nodeParams,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["nodes"] = nodeParams };
        await _client.WriteAsync(CreateNodesCypher, parameters, ct);
    }

    /// <inheritdoc />
    public async Task CreateEdgesAsync(
        List<Dictionary<string, object?>> edgeParams,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["edges"] = edgeParams };
        await _client.WriteAsync(CreateEdgesCypher, parameters, ct);
    }

    /// <inheritdoc />
    public async Task<List<KnowledgeNode>> GetAllNodesAsync(CancellationToken ct = default)
    {
        var results = await _client.ReadAsync(
            GetAllNodesCypher,
            null,
            MapNode,
            ct);

        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<List<KnowledgeDependsOnEdge>> GetAllEdgesAsync(CancellationToken ct = default)
    {
        var results = await _client.ReadAsync(
            GetAllEdgesCypher,
            null,
            MapEdge,
            ct);

        return results.ToList();
    }

    /// <inheritdoc />
    public async Task UpdateNodesAsync(
        List<Dictionary<string, object?>> updateParams,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["updates"] = updateParams };
        await _client.WriteAsync(UpdateNodesCypher, parameters, ct);
    }

    /// <inheritdoc />
    public async Task DeleteNodesAsync(List<string> ids, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["ids"] = ids };
        await _client.WriteAsync(DeleteNodesCypher, parameters, ct);
    }

    /// <inheritdoc />
    public async Task<KnowledgeNode?> GetNodeByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["id"] = id };
        var results = await _client.ReadAsync(GetNodeByIdCypher, parameters, MapNode, ct);
        return results.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<List<string>> GetParentIdsAsync(
        string id,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["id"] = id };
        var results = await _client.ReadAsync(
            GetParentIdsCypher,
            parameters,
            record => record["parentId"].As<string>(),
            ct);
        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<List<string>> GetChildIdsAsync(
        string id,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?> { ["id"] = id };
        var results = await _client.ReadAsync(
            GetChildIdsCypher,
            parameters,
            record => record["childId"].As<string>(),
            ct);
        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<List<KnowledgeNode>> GetNodesByIdsAsync(
        List<string> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        var parameters = new Dictionary<string, object?> { ["ids"] = ids };
        var results = await _client.ReadAsync(GetNodesByIdsCypher, parameters, MapNode, ct);
        return results.ToList();
    }

    private static KnowledgeNode MapNode(IRecord record)
    {
        var node = record["k"].As<INode>();
        var props = node.Properties;

        return new KnowledgeNode(
            Id: props["id"].As<string>(),
            Title: props["title"].As<string>(),
            SessionId: props.GetValueOrDefault("sessionId")?.ToString() ?? string.Empty,
            Description: props["description"].As<string>(),
            DeriveDetails: props["deriveDetails"].As<string>(),
            ResourceUri: props.GetValueOrDefault("resourceUri")?.ToString() ?? string.Empty,
            References: MapReferences(props.GetValueOrDefault("references")),
            LastReviewedAt: props.GetValueOrDefault("lastReviewedAt")?.ToString() ?? string.Empty,
            IsActive: props.GetValueOrDefault("isActive") is bool active && active,
            DeactivateReason: props.GetValueOrDefault("deactivateReason")?.ToString() ?? string.Empty,
            DeactivateAt: props.GetValueOrDefault("deactivateAt")?.ToString() ?? string.Empty,
            CreatedBy: props["createdBy"].As<string>(),
            CreatedAt: props["createdAt"].As<string>(),
            UpdatedBy: props["updatedBy"].As<string>(),
            UpdatedAt: props["updatedAt"].As<string>()
        );
    }

    private static List<string> MapReferences(object? value)
    {
        if (value is IList<object> refs)
            return refs.Select(r => r.ToString()!).ToList();

        return [];
    }

    private static KnowledgeDependsOnEdge MapEdge(IRecord record)
    {
        return new KnowledgeDependsOnEdge(
            FromId: record["fromId"].As<string>(),
            ToId: record["toId"].As<string>(),
            CreatedBy: record["createdBy"].As<string>(),
            CreatedAt: record["createdAt"].As<string>(),
            UpdatedBy: record["updatedBy"].As<string>(),
            UpdatedAt: record["updatedAt"].As<string>()
        );
    }
}
