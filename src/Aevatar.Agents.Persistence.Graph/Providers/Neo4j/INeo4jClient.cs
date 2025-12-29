using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 面向业务的 Neo4j 轻量客户端。
/// </summary>
public interface INeo4jClient
{
    /// <summary>
    /// 执行查询并按自定义映射函数返回类型 <typeparamref name="T" />。
    /// 何时用：自定义 Cypher 查询（含读/写），自行映射记录。
    /// 约束：cypher 需非空；map 不可为 null。
    /// 返回：映射结果列表，无记录返回空列表。
    /// 异常：cypher 为空抛 <see cref="ArgumentException"/>；执行失败抛驱动异常。
    /// </summary>
    /// <param name="cypher">要执行的 Cypher 文本。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="map">记录映射函数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>映射结果列表；无记录则空列表。</returns>
    Task<IReadOnlyList<T>> ReadAsync<T>(
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        Func<IRecord, T> map,
        CancellationToken ct = default);

    /// <summary>
    /// 执行写操作（CREATE/SET/MERGE 等），并等待服务器消费结果。
    /// 约束：cypher 需非空；参数可空。
    /// 返回：任务完成即成功。
    /// 异常：cypher 为空抛 <see cref="ArgumentException"/>；执行失败抛驱动异常。
    /// </summary>
    /// <param name="cypher">写入类 Cypher 文本。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    Task WriteAsync(
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct = default);

    /// <summary>
    /// 执行读取并将指定别名下的节点列映射为 <see cref="GraphNode" />。
    /// 约束：cypher 与 alias 均需非空；参数可空。
    /// 返回：节点列表，无记录返回空列表。
    /// 异常：参数为空抛 <see cref="ArgumentException"/>；执行失败抛驱动异常。
    /// </summary>
    /// <param name="cypher">查询节点的 Cypher。</param>
    /// <param name="alias">记录中节点的别名。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<GraphNode>> ReadNodesAsync(
        string cypher,
        string alias = "n",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// 执行读取并将指定别名下的关系列映射为 <see cref="GraphEdge" />。
    /// 约束：cypher 与 alias 均需非空；参数可空。
    /// 返回：关系列表，无记录返回空列表。
    /// 异常：参数为空抛 <see cref="ArgumentException"/>；执行失败抛驱动异常。
    /// </summary>
    /// <param name="cypher">查询关系的 Cypher。</param>
    /// <param name="alias">记录中关系的别名。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<GraphEdge>> ReadEdgesAsync(
        string cypher,
        string alias = "r",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);
}
