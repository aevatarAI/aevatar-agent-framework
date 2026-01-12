using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.Agents.Persistence.Neo4j;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Neo4j.Graph;

/// <summary>
/// Neo4j Graph Provider 的便捷扩展：将指定 alias 下的 Neo4j Driver 节点/关系映射为框架语义模型。
/// <para>说明：这是 Graph 能力相关 API，因此放在 Neo4j.Graph 项目中，而不是 Neo4j 基础设施项目。</para>
/// </summary>
public static class Neo4jGraphClientExtensions
{
    public static Task<IReadOnlyList<GraphNode>> ReadNodesAsync(
        this INeo4jClient client,
        string cypher,
        string alias = "n",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        EnsureAlias(alias);

        return client.ReadAsync(
            cypher,
            parameters,
            record =>
            {
                var node = record[alias].As<INode>();
                return GraphMapper.ToGraphNode(node);
            },
            ct);
    }

    public static Task<IReadOnlyList<GraphEdge>> ReadEdgesAsync(
        this INeo4jClient client,
        string cypher,
        string alias = "r",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        EnsureAlias(alias);

        return client.ReadAsync(
            cypher,
            parameters,
            record =>
            {
                var relationship = record[alias].As<IRelationship>();
                return GraphMapper.ToGraphEdge(relationship);
            },
            ct);
    }

    private static void EnsureAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Record alias is required.", nameof(alias));
    }
}


