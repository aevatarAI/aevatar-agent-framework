using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.Graph.Abstractions;

/// <summary>
/// Unified graph client for node and edge operations.
/// </summary>
public interface IGraphClient
{
    /// <summary>
    /// 读取单个节点；id 不存在时返回 null。
    /// </summary>
    /// <param name="id">目标节点的 <see cref="NodeId"/>，需为非空字符串。</param>
    /// <returns>找到则返回节点，不存在返回 null。</returns>
    /// <exception cref="Exception">底层存储执行失败时抛出。</exception>
    Task<GraphNode?> ReadAsync(NodeId id);

    /// <summary>
    /// 创建节点；可通过 properties 中的 "id" 指定自定义 id，否则后端生成 UUID。
    /// </summary>
    /// <param name="type">节点类型/标签，必填非空。</param>
    /// <param name="properties">节点属性；可为空字典。</param>
    /// <returns>后端生成的节点 Id。</returns>
    /// <exception cref="InvalidOperationException">后端未返回 id 时抛出。</exception>
    /// <exception cref="Exception">底层执行失败时抛出。</exception>
    Task<NodeId> WriteAsync(string type, IReadOnlyDictionary<string, Value> properties);

    /// <summary>
    /// 更新指定节点属性；id 应存在。
    /// </summary>
    /// <param name="id">目标节点 id。</param>
    /// <param name="properties">要更新的属性键值。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task UpdateAsync(NodeId id, IReadOnlyDictionary<string, Value> properties);

    /// <summary>
    /// 删除指定节点；不存在时后端可能静默忽略。
    /// </summary>
    /// <param name="id">目标节点 id。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task DeleteAsync(NodeId id);

    /// <summary>
    /// 按类型及条件批量删除节点（DETACH 语义：会同时移除其关联的关系）。
    /// </summary>
    /// <param name="query">包含 Type（必填）与 Conditions 的删除条件。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task DeleteAsync(NodeQuery query);

    /// <summary>
    /// 按类型及条件查询节点。
    /// </summary>
    /// <param name="query">包含 Type（必填）与 Conditions 的查询。</param>
    /// <returns>匹配列表，未命中返回空集合。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task<IReadOnlyList<GraphNode>> QueryAsync(NodeQuery query);

    /// <summary>
    /// 读取单个关系；id 不存在时返回 null。
    /// </summary>
    /// <param name="id">目标关系 id。</param>
    /// <returns>找到则返回关系，不存在返回 null。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task<GraphEdge?> ReadAsync(EdgeId id);

    /// <summary>
    /// 创建关系；from/to 必须指向已存在节点，可在 properties 中指定 "id"，否则后端生成。
    /// </summary>
    /// <param name="type">关系类型/标签，必填。</param>
    /// <param name="from">起点节点 id。</param>
    /// <param name="to">终点节点 id。</param>
    /// <param name="properties">关系属性。</param>
    /// <returns>后端生成的关系 Id。</returns>
    /// <exception cref="InvalidOperationException">后端未返回 id 时抛出。</exception>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task<EdgeId> WriteAsync(string type, NodeId from, NodeId to, IReadOnlyDictionary<string, Value> properties);

    /// <summary>
    /// 更新指定关系属性；id 应存在。
    /// </summary>
    /// <param name="id">目标关系 id。</param>
    /// <param name="properties">要更新的属性键值。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task UpdateAsync(EdgeId id, IReadOnlyDictionary<string, Value> properties);

    /// <summary>
    /// 删除指定关系；不存在时后端可能静默忽略。
    /// </summary>
    /// <param name="id">目标关系 id。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task DeleteAsync(EdgeId id);

    /// <summary>
    /// 按类型及条件查询关系。
    /// </summary>
    /// <param name="query">包含可选 Type 与 Conditions 的查询。</param>
    /// <returns>匹配列表，未命中返回空集合。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task<IReadOnlyList<GraphEdge>> QueryAsync(EdgeQuery query);

    /// <summary>
    /// 按类型及条件删除关系（批量）。
    /// </summary>
    /// <param name="query">包含可选 Type 与 Conditions 的删除条件。</param>
    /// <returns>完成任务即代表执行成功。</returns>
    /// <exception cref="Exception">执行失败时抛出。</exception>
    Task DeleteAsync(EdgeQuery query);
}
