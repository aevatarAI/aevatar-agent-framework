using System.Collections.Concurrent;
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// InMemory 图数据存储（线程安全的最小实现）。
/// </summary>
internal sealed class InMemoryGraphStore
{
    private readonly ConcurrentDictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GraphEdge> _edges = new(StringComparer.Ordinal);

    public bool TryGetNode(NodeId id, out GraphNode node) => _nodes.TryGetValue(id.Value, out node!);
    public bool TryGetEdge(EdgeId id, out GraphEdge edge) => _edges.TryGetValue(id.Value, out edge!);

    public void UpsertNode(GraphNode node) => _nodes[node.Id.Value] = node;
    public void UpsertEdge(GraphEdge edge) => _edges[edge.Id.Value] = edge;

    public bool RemoveNode(NodeId id) => _nodes.TryRemove(id.Value, out _);
    public bool RemoveEdge(EdgeId id) => _edges.TryRemove(id.Value, out _);

    public IEnumerable<GraphNode> Nodes => _nodes.Values;
    public IEnumerable<GraphEdge> Edges => _edges.Values;
}


