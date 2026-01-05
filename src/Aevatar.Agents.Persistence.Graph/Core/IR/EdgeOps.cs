using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.Graph.Core.IR;

/// <summary>读取指定关系；不存在返回 null。</summary>
/// <param name="Id">要读取的关系 Id。</param>
public sealed record ReadEdge(EdgeId Id) : GraphOperation;
/// <summary>创建关系；Props 可含自定义 "id"，否则后端生成。</summary>
/// <param name="Type">关系类型/标签。</param>
/// <param name="From">起点节点 Id。</param>
/// <param name="To">终点节点 Id。</param>
/// <param name="Props">属性字典，可含 "id"。</param>
public sealed record CreateEdge(string Type, NodeId From, NodeId To, IReadOnlyDictionary<string, Value> Props) : GraphOperation;
/// <summary>更新关系属性；Id 应存在。</summary>
/// <param name="Id">要更新的关系 Id。</param>
/// <param name="Props">要写入的属性键值。</param>
public sealed record UpdateEdge(EdgeId Id, IReadOnlyDictionary<string, Value> Props) : GraphOperation;
/// <summary>删除关系；Id 不存在时后端可能忽略。</summary>
/// <param name="Id">要删除的关系 Id。</param>
public sealed record DeleteEdge(EdgeId Id) : GraphOperation;
/// <summary>按类型与条件查询关系。</summary>
/// <param name="Query">包含可选 Type 与 Conditions 的查询。</param>
public sealed record QueryEdges(EdgeQuery Query) : GraphOperation;
/// <summary>按类型与条件删除关系（批量）。</summary>
/// <param name="Query">包含可选 Type 与 Conditions 的删除条件。</param>
public sealed record DeleteEdges(EdgeQuery Query) : GraphOperation;
