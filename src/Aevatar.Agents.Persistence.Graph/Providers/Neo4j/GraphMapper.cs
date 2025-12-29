using System.Collections.Immutable;
using System.Linq;
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 将 Neo4j Driver 对象转换为框架内部模型。
/// </summary>
public static class GraphMapper
{
    /// <summary>
    /// 将 Neo4j 节点映射为框架内部的 <see cref="GraphNode" />。
    /// 规则：优先读取属性 "id" 作为返回的 <see cref="NodeId"/>，缺失时回退 ElementId。
    /// 属性转换：字典值被转为 <see cref="Value"/> 派生类型。
    /// </summary>
    /// <param name="node">来自 Neo4j.Driver 的节点实例，不能为空。</param>
    /// <returns>转换后的 <see cref="GraphNode"/>。</returns>
    /// <exception cref="ArgumentNullException">node 为 null 时抛出。</exception>
    public static GraphNode ToGraphNode(INode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var primaryLabel = node.Labels.FirstOrDefault() ?? "node";
        var elementId = ResolveNodeElementId(node);
        var propertyId = ResolveIdFromProperties(node.Properties);
        var nodeId = new Abstractions.NodeId(propertyId ?? elementId);
        return new GraphNode(
            nodeId,
            primaryLabel,
            ToAttributes(node.Properties).ToDictionary(k => k.Key, v => ToSemanticValue(v.Value)));
    }

    /// <summary>
    /// 将 Neo4j 关系映射为框架内部的 <see cref="GraphEdge" />。
    /// 规则：优先读取属性 "id" 作为 <see cref="EdgeId"/>，缺失时回退 ElementId；起止节点 id 使用 ElementId。
    /// 属性转换：字典值被转为 <see cref="Value"/> 派生类型。
    /// </summary>
    /// <param name="relationship">来自 Neo4j.Driver 的关系实例，不能为空。</param>
    /// <returns>转换后的 <see cref="GraphEdge"/>。</returns>
    /// <exception cref="ArgumentNullException">relationship 为 null 时抛出。</exception>
    public static GraphEdge ToGraphEdge(IRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        var start = ResolveStartNodeElementId(relationship);
        var end = ResolveEndNodeElementId(relationship);
        var edgePropertyId = ResolveIdFromProperties(relationship.Properties);

        return new GraphEdge(
            new Abstractions.EdgeId(edgePropertyId ?? ResolveRelationshipElementId(relationship)),
            relationship.Type,
            new Abstractions.NodeId(start),
            new Abstractions.NodeId(end),
            ToAttributes(relationship.Properties).ToDictionary(k => k.Key, v => ToSemanticValue(v.Value)));
    }

    private static string ResolveNodeElementId(INode node)
    {
        if (!string.IsNullOrWhiteSpace(node.ElementId))
        {
            return node.ElementId;
        }

#pragma warning disable CS0618
        return node.Id.ToString();
#pragma warning restore CS0618
    }

    private static string ResolveStartNodeElementId(IRelationship relationship)
    {
        if (!string.IsNullOrWhiteSpace(relationship.StartNodeElementId))
        {
            return relationship.StartNodeElementId;
        }

#pragma warning disable CS0618
        return relationship.StartNodeId.ToString();
#pragma warning restore CS0618
    }

    private static string ResolveEndNodeElementId(IRelationship relationship)
    {
        if (!string.IsNullOrWhiteSpace(relationship.EndNodeElementId))
        {
            return relationship.EndNodeElementId;
        }

#pragma warning disable CS0618
        return relationship.EndNodeId.ToString();
#pragma warning restore CS0618
    }

    private static string ResolveRelationshipElementId(IRelationship relationship)
    {
        if (!string.IsNullOrWhiteSpace(relationship.ElementId))
        {
            return relationship.ElementId;
        }

#pragma warning disable CS0618
        return relationship.Id.ToString();
#pragma warning restore CS0618
    }

    private static string? ResolveIdFromProperties(IReadOnlyDictionary<string, object> properties)
    {
        if (properties is null) return null;
        if (properties.TryGetValue("id", out var idVal) && idVal is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?> ToAttributes(IReadOnlyDictionary<string, object> properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return ImmutableDictionary<string, object?>.Empty;
        }

        return properties.ToImmutableDictionary(k => k.Key, v => (object?)v.Value);
    }

    private static Value ToSemanticValue(object? val) => val switch
    {
        null => new StringValue(string.Empty),
        string s => new StringValue(s),
        bool b => new BoolValue(b),
        int i => new IntValue(i),
        long l => new IntValue(l),
        double d => new FloatValue(d),
        float f => new FloatValue(f),
        IDictionary<string, object> dict => new MapValue(dict.ToDictionary(k => k.Key, v => ToSemanticValue(v.Value))),
        _ => new StringValue(val.ToString() ?? string.Empty)
    };
}
