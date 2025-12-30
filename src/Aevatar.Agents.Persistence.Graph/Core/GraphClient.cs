using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.Graph.Core;

/// <summary>
/// 统一图客户端实现，组合编译器与执行器处理节点/关系的 CRUD 与查询。
/// <para>通过 DI 解析为 <see cref="IGraphClient"/> 使用。</para>
/// </summary>
public sealed class GraphClient<TCommand> : IGraphClient
{
    private readonly IGraphCompiler<TCommand> _compiler;
    private readonly IGraphExecutor<TCommand> _executor;

    /// <summary>
    /// 创建 GraphClient。
    /// </summary>
    /// <param name="compiler">将图操作编译为后端命令的编译器。</param>
    /// <param name="executor">执行编译结果的执行器。</param>
    public GraphClient(
        IGraphCompiler<TCommand> compiler,
        IGraphExecutor<TCommand> executor)
    {
        _compiler = compiler;
        _executor = executor;
    }

    // ── Node operations ──
    /// <inheritdoc />
    public Task<GraphNode?> ReadAsync(NodeId id) =>
        Execute<GraphNode>(new ReadNode(id));

    /// <inheritdoc />
    public async Task<NodeId> WriteAsync(string type, IReadOnlyDictionary<string, Value> properties)
    {
        var created = await Execute<NodeId?>(new CreateNode(type, properties));
        return created ?? throw new InvalidOperationException("Failed to create node id from backend.");
    }

    /// <inheritdoc />
    public Task UpdateAsync(NodeId id, IReadOnlyDictionary<string, Value> properties) =>
        Execute<object>(new UpdateNode(id, properties));

    /// <inheritdoc />
    public Task DeleteAsync(NodeId id) =>
        Execute<object>(new DeleteNode(id));

    /// <inheritdoc />
    public Task DeleteAsync(NodeQuery query) =>
        Execute<object>(new DeleteNodes(query));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> QueryAsync(NodeQuery query)
    {
        var result = await Execute<IReadOnlyList<GraphNode>>(new QueryNodes(query));
        return result ?? Array.Empty<GraphNode>();
    }

    // ── Edge operations ──
    /// <inheritdoc />
    public Task<GraphEdge?> ReadAsync(EdgeId id) =>
        Execute<GraphEdge>(new ReadEdge(id));

    /// <inheritdoc />
    public async Task<EdgeId> WriteAsync(string type, NodeId from, NodeId to, IReadOnlyDictionary<string, Value> properties)
    {
        var created = await Execute<EdgeId?>(new CreateEdge(type, from, to, properties));
        return created ?? throw new InvalidOperationException("Failed to create edge id from backend.");
    }

    /// <inheritdoc />
    public Task UpdateAsync(EdgeId id, IReadOnlyDictionary<string, Value> properties) =>
        Execute<object>(new UpdateEdge(id, properties));

    /// <inheritdoc />
    public Task DeleteAsync(EdgeId id) =>
        Execute<object>(new DeleteEdge(id));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphEdge>> QueryAsync(EdgeQuery query)
    {
        var result = await Execute<IReadOnlyList<GraphEdge>>(new QueryEdges(query));
        return result ?? Array.Empty<GraphEdge>();
    }

    /// <inheritdoc />
    public Task DeleteAsync(EdgeQuery query) =>
        Execute<object>(new DeleteEdges(query));

    // ── Shared executor ──
    private async Task<T?> Execute<T>(GraphOperation op)
    {
        var plan = new GraphPlan { Operation = op };
        var compiled = _compiler.Compile(plan);
        return (T?)await _executor.ExecuteAsync(compiled);
    }
}
