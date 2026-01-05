namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

using Aevatar.Agents.Persistence.Graph.Abstractions;

/// <summary>
/// 图节点语义模型：包含强类型 Id、类型/标签以及属性字典。
/// <para>属性键必须与持久化层使用的一致；值使用 <see cref="Value"/> 派生类型封装。</para>
/// </summary>
/// <param name="Id">节点标识（优先来自属性 id）。</param>
/// <param name="Type">节点类型/标签。</param>
/// <param name="Properties">属性字典，可为空集合。</param>
public sealed record GraphNode(
    NodeId Id,
    string Type,
    IReadOnlyDictionary<string, Value> Properties
);
