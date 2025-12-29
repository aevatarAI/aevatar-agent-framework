using System.Collections.Immutable;
using System.Linq;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 小而完整的 Neo4j 客户端，封装基础的读写与模型转换。
/// </summary>
public sealed class Neo4jClient : INeo4jClient
{
    private readonly INeo4jSessionFactory _sessionFactory;

    /// <summary>
    /// 创建 Neo4j 轻量客户端。
    /// </summary>
    /// <param name="sessionFactory">提供读/写 Session 的工厂，必非 null。</param>
    public Neo4jClient(INeo4jSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    /// <summary>
    /// 执行查询并按映射函数返回列表；允许包含写入语句。
    /// </summary>
    /// <param name="cypher">要执行的 Cypher，不能为空。</param>
    /// <param name="parameters">可选参数字典；null/空会被归一化为空字典。</param>
    /// <param name="map">记录映射函数，不能为空。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>映射后的结果列表；无记录返回空列表。</returns>
    /// <exception cref="ArgumentException">cypher 为空时抛出。</exception>
    /// <exception cref="ArgumentNullException">map 为 null 时抛出。</exception>
    /// <exception cref="Exception">驱动执行失败时抛出。</exception>
    public Task<IReadOnlyList<T>> ReadAsync<T>(
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        Func<IRecord, T> map,
        CancellationToken ct = default)
    {
        ValidateCypher(cypher);
        ArgumentNullException.ThrowIfNull(map);

        // 使用 Write 模式以兼容含有 CREATE/MERGE/SET 的查询（Neo4j Aura 对读会话禁止写入）。
        return _sessionFactory.ExecuteWriteAsync(async session =>
        {
            ct.ThrowIfCancellationRequested();
            var cursor = await session.RunAsync(cypher, NormalizeParameters(parameters));

            var results = new List<T>();
            while (await cursor.FetchAsync())
            {
                results.Add(map(cursor.Current));
            }

            return (IReadOnlyList<T>)results;
        }, ct);
    }

    /// <summary>
    /// 执行写入/更新语句并消费结果。
    /// </summary>
    /// <param name="cypher">写入类 Cypher，不能为空。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>任务完成即代表执行成功。</returns>
    /// <exception cref="ArgumentException">cypher 为空时抛出。</exception>
    /// <exception cref="Exception">驱动执行失败时抛出。</exception>
    public async Task WriteAsync(
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct = default)
    {
        ValidateCypher(cypher);

        await _sessionFactory.ExecuteWriteAsync(async session =>
        {
            ct.ThrowIfCancellationRequested();
            var cursor = await session.RunAsync(cypher, NormalizeParameters(parameters));
            await cursor.ConsumeAsync();
            return true;
        }, ct);
    }

    /// <summary>
    /// 读取节点列并映射为 <see cref="GraphNode" />。
    /// </summary>
    /// <param name="cypher">查询节点的 Cypher，不能为空。</param>
    /// <param name="alias">返回记录中节点的别名，不能为空（默认 "n"）。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>节点列表，无记录返回空列表。</returns>
    /// <exception cref="ArgumentException">cypher 或 alias 为空时抛出。</exception>
    /// <exception cref="Exception">驱动执行失败时抛出。</exception>
    public Task<IReadOnlyList<GraphNode>> ReadNodesAsync(
        string cypher,
        string alias = "n",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        EnsureAlias(alias);
        return ReadAsync(cypher, parameters, record =>
        {
            var node = record[alias].As<INode>();
            return GraphMapper.ToGraphNode(node);
        }, ct);
    }

    /// <summary>
    /// 读取关系列并映射为 <see cref="GraphEdge" />。
    /// </summary>
    /// <param name="cypher">查询关系的 Cypher，不能为空。</param>
    /// <param name="alias">返回记录中关系的别名，不能为空（默认 "r"）。</param>
    /// <param name="parameters">可选参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>关系列表，无记录返回空列表。</returns>
    /// <exception cref="ArgumentException">cypher 或 alias 为空时抛出。</exception>
    /// <exception cref="Exception">驱动执行失败时抛出。</exception>
    public Task<IReadOnlyList<GraphEdge>> ReadEdgesAsync(
        string cypher,
        string alias = "r",
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        EnsureAlias(alias);
        return ReadAsync(cypher, parameters, record =>
        {
            var relationship = record[alias].As<IRelationship>();
            return GraphMapper.ToGraphEdge(relationship);
        }, ct);
    }

    private static void ValidateCypher(string cypher)
    {
        if (string.IsNullOrWhiteSpace(cypher))
        {
            throw new ArgumentException("Cypher text is required.", nameof(cypher));
        }
    }

    private static void EnsureAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Record alias is required.", nameof(alias));
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeParameters(
        IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return ImmutableDictionary<string, object?>.Empty;
        }

        return parameters is ImmutableDictionary<string, object?> immutable
            ? immutable
            : parameters.ToDictionary(k => k.Key, v => v.Value);
    }
}
