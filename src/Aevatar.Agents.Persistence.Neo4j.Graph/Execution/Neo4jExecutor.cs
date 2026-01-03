using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Neo4j;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Neo4j.Graph;

/// <summary>
/// Executes compiled Cypher commands via INeo4jClient.
/// </summary>
public sealed class Neo4jExecutor : IGraphExecutor<CypherCommand>
{
    private readonly INeo4jClient _client;

    /// <summary>
    /// 创建 Neo4j 执行器。
    /// </summary>
    /// <param name="client">底层 Neo4j 客户端。</param>
    public Neo4jExecutor(INeo4jClient client)
    {
        _client = client;
    }

    /// <summary>
    /// 执行编译的 CypherCommand，并将结果映射为语义对象或标识。
    /// </summary>
    /// <param name="compiled">由编译器生成的命令，需包含操作类型。</param>
    /// <returns>
    /// 根据操作返回 GraphNode/GraphEdge/NodeId/EdgeId/列表，或非查询写操作返回 null。
    /// </returns>
    /// <exception cref="Exception">底层 Neo4j 执行失败时抛出。</exception>
    public async Task<object?> ExecuteAsync(CypherCommand compiled)
    {
        return compiled.Operation switch
        {
            ReadNode => await ReadNodeAsync(compiled),
            CreateNode => await CreateNodeAsync(compiled),
            UpdateNode or DeleteNode => await ExecNonQueryAsync(compiled),
            QueryNodes => await QueryNodesAsync(compiled),
            ReadEdge => await ReadEdgeAsync(compiled),
            CreateEdge => await CreateEdgeAsync(compiled),
            UpdateEdge or DeleteEdge => await ExecNonQueryAsync(compiled),
            ReadEdgesBetween => await ReadEdgesBetweenAsync(compiled),
            _ => null
        };
    }

    private async Task<object?> ReadNodeAsync(CypherCommand cmd)
    {
        var nodes = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["n"].As<global::Neo4j.Driver.INode>(),
            CancellationToken.None);

        if (nodes.Count == 0) return null;
        var node = nodes[0];
        return ToGraphNode(node);
    }

    private async Task<object?> QueryNodesAsync(CypherCommand cmd)
    {
        var nodes = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["n"].As<global::Neo4j.Driver.INode>(),
            CancellationToken.None);

        return nodes.Select(GraphMapper.ToGraphNode).ToList();
    }

    private async Task<object?> ReadEdgeAsync(CypherCommand cmd)
    {
        var edges = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["r"].As<global::Neo4j.Driver.IRelationship>(),
            CancellationToken.None);

        if (edges.Count == 0) return null;
        return ToGraphEdge(edges[0]);
    }

    private async Task<object?> ReadEdgesBetweenAsync(CypherCommand cmd)
    {
        var edges = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["r"].As<global::Neo4j.Driver.IRelationship>(),
            CancellationToken.None);

        return edges.Select(GraphMapper.ToGraphEdge).ToList();
    }

    private async Task<object?> CreateNodeAsync(CypherCommand cmd)
    {
        var nodes = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["n"].As<global::Neo4j.Driver.INode>(),
            CancellationToken.None);
        if (nodes.Count == 0) return null;
        var node = nodes[0];
        var mapped = GraphMapper.ToGraphNode(node);
        return mapped.Id;
    }

    private async Task<object?> CreateEdgeAsync(CypherCommand cmd)
    {
        var edges = await _client.ReadAsync(
            cmd.Text,
            cmd.Parameters,
            r => r["r"].As<global::Neo4j.Driver.IRelationship>(),
            CancellationToken.None);
        if (edges.Count == 0) return null;
        var rel = edges[0];
        var mapped = GraphMapper.ToGraphEdge(rel);
        return mapped.Id;
    }

    private async Task<object?> ExecNonQueryAsync(CypherCommand cmd)
    {
        await _client.WriteAsync(cmd.Text, cmd.Parameters, CancellationToken.None);
        return null;
    }

    private static GraphNode ToGraphNode(global::Neo4j.Driver.INode node) => GraphMapper.ToGraphNode(node);
    private static GraphEdge ToGraphEdge(global::Neo4j.Driver.IRelationship rel) => GraphMapper.ToGraphEdge(rel);
}
