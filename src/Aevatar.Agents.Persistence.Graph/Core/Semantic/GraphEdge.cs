namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

using Aevatar.Agents.Persistence.Graph.Abstractions;

/// <summary>
/// 图关系语义模型：包含关系 Id、类型、起止节点 Id 以及属性字典。
/// <para>属性键需与存储一致；值使用 <see cref="Value"/> 派生类型封装。</para>
/// </summary>
/// <param name="Id">关系标识（优先来自属性 id）。</param>
/// <param name="Type">关系类型/标签。</param>
/// <param name="From">起点节点 Id。</param>
/// <param name="To">终点节点 Id。</param>
/// <param name="Properties">属性字典，可为空集合。</param>
public sealed record GraphEdge(
    EdgeId Id,
    string Type,
    NodeId From,
    NodeId To,
    IReadOnlyDictionary<string, Value> Properties
);
