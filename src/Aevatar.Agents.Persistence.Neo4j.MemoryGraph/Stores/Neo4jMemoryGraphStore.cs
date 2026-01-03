using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.Neo4j;
using Google.Protobuf.WellKnownTypes;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;

using MemoryGraphMessage = global::Aevatar.Agents.Abstractions.Memory.MemoryGraph;
using MemoryGraphNodeMessage = global::Aevatar.Agents.Abstractions.Memory.MemoryGraphNode;
using MemoryGraphEdgeMessage = global::Aevatar.Agents.Abstractions.Memory.MemoryGraphEdge;

/// <summary>
/// 将 <see cref="MemoryGraph"/> 落到 Neo4j 的实现。
/// <para>
/// 设计取舍：
/// - 节点/边都以“固定 label/relType + 属性 type/label”的方式存储，避免动态类型注入风险。
/// - 每次 Save 视为“全量覆盖”：先按 graphId 删除旧子图，再批量写入新 nodes/edges。
/// </para>
/// </summary>
public sealed class Neo4jMemoryGraphStore : IMemoryGraphStore
{
    // ----------------------------
    //  Neo4j schema conventions
    // ----------------------------
    // Labels
    public const string GraphLabel = "AevatarMemoryGraph";
    public const string NodeLabel = "AevatarMemoryGraphNode";
    // Relationship type (fixed)
    public const string EdgeRelType = "AEVATAR_MEMORY_GRAPH_EDGE";

    private readonly INeo4jClient _client;

    public Neo4jMemoryGraphStore(INeo4jClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task SaveAsync(MemoryGraphMessage graph, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(graph);

        var graphId = (graph.GraphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(graphId))
            throw new ArgumentException("MemoryGraph.graph_id cannot be empty.", nameof(graph));

        var scopeType = (int)(graph.Scope?.Type ?? MemoryScopeType.Unspecified);
        var scopeId = (graph.Scope?.ScopeId ?? string.Empty).Trim();

        var createdAtMs = ToUnixMs(graph.CreatedAt) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var graphLabelsJson = SerializeLabels(graph.Labels);

        var nodes = BuildNodeRows(graph.Nodes);
        var edges = BuildEdgeRows(graph.Edges);

        var cypher = $$"""
                     MERGE (g:{{GraphLabel}} { graphId: $graphId })
                     SET g.scopeType = $scopeType,
                         g.scopeId = $scopeId,
                         g.createdAtUnixMs = $createdAtUnixMs,
                         g.updatedAtUnixMs = $updatedAtUnixMs,
                         g.labelsJson = $graphLabelsJson,
                         g.nodeCount = $nodeCount,
                         g.edgeCount = $edgeCount
                     WITH g
                     MATCH (n:{{NodeLabel}} { graphId: $graphId })
                     DETACH DELETE n
                     WITH g
                     UNWIND $nodes AS node
                     MERGE (n:{{NodeLabel}} { graphId: $graphId, nodeId: node.nodeId })
                     SET n.type = node.type,
                         n.name = node.name,
                         n.content = node.content,
                         n.labelsJson = node.labelsJson
                     WITH g
                     UNWIND $edges AS e
                     MATCH (from:{{NodeLabel}} { graphId: $graphId, nodeId: e.fromNodeId })
                     MATCH (to:{{NodeLabel}} { graphId: $graphId, nodeId: e.toNodeId })
                     MERGE (from)-[r:{{EdgeRelType}} { graphId: $graphId, edgeId: e.edgeId }]->(to)
                     SET r.type = e.type,
                         r.label = e.label,
                         r.labelsJson = e.labelsJson
                     RETURN 1
                     """;

        await _client.WriteAsync(
            cypher,
            new Dictionary<string, object?>
            {
                ["graphId"] = graphId,
                ["scopeType"] = scopeType,
                ["scopeId"] = scopeId,
                ["createdAtUnixMs"] = createdAtMs,
                ["updatedAtUnixMs"] = updatedAtMs,
                ["graphLabelsJson"] = graphLabelsJson,
                ["nodeCount"] = nodes.Count,
                ["edgeCount"] = edges.Count,
                ["nodes"] = nodes,
                ["edges"] = edges
            },
            ct);
    }

    public async Task<MemoryGraphMessage?> LoadAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        graphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(graphId))
            return null;

        var meta = await LoadMetaAsync(graphId, ct);
        if (meta == null)
            return null;

        var nodes = await LoadNodesAsync(graphId, ct);
        var edges = await LoadEdgesAsync(graphId, ct);

        var graph = new MemoryGraphMessage
        {
            GraphId = graphId,
            Scope = new MemoryScope
            {
                Type = (MemoryScopeType)meta.ScopeType,
                ScopeId = meta.ScopeId
            },
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(
                DateTimeOffset.FromUnixTimeMilliseconds(meta.CreatedAtUnixMs).UtcDateTime,
                DateTimeKind.Utc))
        };

        foreach (var (k, v) in DeserializeLabels(meta.LabelsJson))
            graph.Labels[k] = v;

        foreach (var n in nodes)
        {
            var node = new MemoryGraphNodeMessage
            {
                NodeId = n.NodeId,
                Type = n.Type,
                Name = n.Name,
                Content = n.Content
            };
            foreach (var (k, v) in DeserializeLabels(n.LabelsJson))
                node.Labels[k] = v;
            graph.Nodes.Add(node);
        }

        foreach (var e in edges)
        {
            var edge = new MemoryGraphEdgeMessage
            {
                EdgeId = e.EdgeId,
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                Type = e.Type,
                Label = e.Label
            };
            foreach (var (k, v) in DeserializeLabels(e.LabelsJson))
                edge.Labels[k] = v;
            graph.Edges.Add(edge);
        }

        return graph;
    }

    public async Task<bool> ExistsAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        graphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(graphId))
            return false;

        var cypher = $$"""
                     MATCH (g:{{GraphLabel}} { graphId: $graphId })
                     RETURN count(g) > 0 AS exists
                     """;

        var list = await _client.ReadAsync(
            cypher,
            new Dictionary<string, object?> { ["graphId"] = graphId },
            r => r["exists"].As<bool>(),
            ct);

        return list.Count > 0 && list[0];
    }

    public async Task DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        graphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(graphId))
            return;

        var cypher = $$"""
                     OPTIONAL MATCH (n:{{NodeLabel}} { graphId: $graphId })
                     DETACH DELETE n
                     WITH 1 AS _
                     OPTIONAL MATCH (g:{{GraphLabel}} { graphId: $graphId })
                     DETACH DELETE g
                     """;

        await _client.WriteAsync(
            cypher,
            new Dictionary<string, object?> { ["graphId"] = graphId },
            ct);
    }

    // ============================================================
    //  Load helpers
    // ============================================================

    private async Task<GraphMetaRow?> LoadMetaAsync(string graphId, CancellationToken ct)
    {
        var cypher = $$"""
                     MATCH (g:{{GraphLabel}} { graphId: $graphId })
                     RETURN
                       g.scopeType AS scopeType,
                       g.scopeId AS scopeId,
                       g.createdAtUnixMs AS createdAtUnixMs,
                       g.labelsJson AS labelsJson
                     LIMIT 1
                     """;

        var rows = await _client.ReadAsync(
            cypher,
            new Dictionary<string, object?> { ["graphId"] = graphId },
            r => new GraphMetaRow(
                ScopeType: ToInt(r["scopeType"]),
                ScopeId: r["scopeId"].As<string>() ?? string.Empty,
                CreatedAtUnixMs: ToLong(r["createdAtUnixMs"]),
                LabelsJson: r["labelsJson"].As<string>() ?? string.Empty),
            ct);

        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<IReadOnlyList<NodeRow>> LoadNodesAsync(string graphId, CancellationToken ct)
    {
        var cypher = $$"""
                     MATCH (n:{{NodeLabel}} { graphId: $graphId })
                     RETURN
                       n.nodeId AS nodeId,
                       n.type AS type,
                       n.name AS name,
                       n.content AS content,
                       n.labelsJson AS labelsJson
                     """;

        return await _client.ReadAsync(
            cypher,
            new Dictionary<string, object?> { ["graphId"] = graphId },
            r => new NodeRow(
                NodeId: r["nodeId"].As<string>() ?? string.Empty,
                Type: r["type"].As<string>() ?? string.Empty,
                Name: r["name"].As<string>() ?? string.Empty,
                Content: r["content"].As<string>() ?? string.Empty,
                LabelsJson: r["labelsJson"].As<string>() ?? string.Empty),
            ct);
    }

    private async Task<IReadOnlyList<EdgeRow>> LoadEdgesAsync(string graphId, CancellationToken ct)
    {
        var cypher = $$"""
                     MATCH (from:{{NodeLabel}} { graphId: $graphId })-[r:{{EdgeRelType}} { graphId: $graphId }]->(to:{{NodeLabel}} { graphId: $graphId })
                     RETURN
                       r.edgeId AS edgeId,
                       from.nodeId AS fromNodeId,
                       to.nodeId AS toNodeId,
                       r.type AS type,
                       r.label AS label,
                       r.labelsJson AS labelsJson
                     """;

        return await _client.ReadAsync(
            cypher,
            new Dictionary<string, object?> { ["graphId"] = graphId },
            r => new EdgeRow(
                EdgeId: r["edgeId"].As<string>() ?? string.Empty,
                FromNodeId: r["fromNodeId"].As<string>() ?? string.Empty,
                ToNodeId: r["toNodeId"].As<string>() ?? string.Empty,
                Type: r["type"].As<string>() ?? string.Empty,
                Label: r["label"].As<string>() ?? string.Empty,
                LabelsJson: r["labelsJson"].As<string>() ?? string.Empty),
            ct);
    }

    // ============================================================
    //  Serialize helpers
    // ============================================================

    private static List<Dictionary<string, object?>> BuildNodeRows(IEnumerable<MemoryGraphNodeMessage> nodes)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var n in nodes ?? Array.Empty<MemoryGraphNodeMessage>())
        {
            var nodeId = (n.NodeId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nodeId)) continue;

            list.Add(new Dictionary<string, object?>
            {
                ["nodeId"] = nodeId,
                ["type"] = (n.Type ?? string.Empty).Trim(),
                ["name"] = (n.Name ?? string.Empty).Trim(),
                ["content"] = (n.Content ?? string.Empty).Trim(),
                ["labelsJson"] = SerializeLabels(n.Labels)
            });
        }

        return list;
    }

    private static List<Dictionary<string, object?>> BuildEdgeRows(IEnumerable<MemoryGraphEdgeMessage> edges)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var e in edges ?? Array.Empty<MemoryGraphEdgeMessage>())
        {
            var edgeId = (e.EdgeId ?? string.Empty).Trim();
            var from = (e.FromNodeId ?? string.Empty).Trim();
            var to = (e.ToNodeId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(edgeId) ||
                string.IsNullOrWhiteSpace(from) ||
                string.IsNullOrWhiteSpace(to))
            {
                continue;
            }

            list.Add(new Dictionary<string, object?>
            {
                ["edgeId"] = edgeId,
                ["fromNodeId"] = from,
                ["toNodeId"] = to,
                ["type"] = (e.Type ?? string.Empty).Trim(),
                ["label"] = (e.Label ?? string.Empty).Trim(),
                ["labelsJson"] = SerializeLabels(e.Labels)
            });
        }

        return list;
    }

    private static string SerializeLabels(IDictionary<string, string> labels)
    {
        if (labels == null || labels.Count == 0)
            return string.Empty;

        try
        {
            return JsonSerializer.Serialize(labels);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeLabels(string? labelsJson)
    {
        if (string.IsNullOrWhiteSpace(labelsJson))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(labelsJson!)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static long? ToUnixMs(Timestamp? ts)
    {
        if (ts == null)
            return null;

        try
        {
            var dt = ts.ToDateTime();
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }
        catch
        {
            return null;
        }
    }

    private static long ToLong(object? value)
    {
        return value switch
        {
            null => 0,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d => (long)d,
            float f => (long)f,
            _ => long.TryParse(value.ToString(), out var l2) ? l2 : 0
        };
    }

    private static int ToInt(object? value)
    {
        return value switch
        {
            null => 0,
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            double d => (int)d,
            float f => (int)f,
            _ => int.TryParse(value.ToString(), out var i2) ? i2 : 0
        };
    }

    // ============================================================
    //  Internal DTOs (for query mapping)
    // ============================================================

    private sealed record GraphMetaRow(int ScopeType, string ScopeId, long CreatedAtUnixMs, string LabelsJson);
    private sealed record NodeRow(string NodeId, string Type, string Name, string Content, string LabelsJson);
    private sealed record EdgeRow(string EdgeId, string FromNodeId, string ToNodeId, string Type, string Label, string LabelsJson);
}


