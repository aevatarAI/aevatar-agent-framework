using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Persistence.Graph.Core.IR;

/// <summary>读取指定节点；不存在返回 null。</summary>
/// <param name="Id">要读取的节点 Id。</param>
public sealed record ReadNode(NodeId Id) : GraphOperation;
/// <summary>创建节点；Props 可包含自定义 "id"，否则后端生成。</summary>
/// <param name="Type">节点类型/标签。</param>
/// <param name="Props">属性字典，可空/包含 "id"。</param>
public sealed record CreateNode(string Type, IReadOnlyDictionary<string, Value> Props) : GraphOperation;
/// <summary>更新节点属性；Id 应存在。</summary>
/// <param name="Id">要更新的节点 Id。</param>
/// <param name="Props">要写入的属性键值。</param>
public sealed record UpdateNode(NodeId Id, IReadOnlyDictionary<string, Value> Props) : GraphOperation;
/// <summary>删除节点；Id 不存在时后端可能忽略。</summary>
/// <param name="Id">要删除的节点 Id。</param>
public sealed record DeleteNode(NodeId Id) : GraphOperation;
/// <summary>按类型与条件批量删除节点（DETACH 语义）。</summary>
/// <param name="Query">包含 Type 与 Conditions 的删除条件。</param>
public sealed record DeleteNodes(NodeQuery Query) : GraphOperation;
/// <summary>按类型与条件查询节点。</summary>
/// <param name="Query">包含 Type 与 Conditions 的查询。</param>
public sealed record QueryNodes(NodeQuery Query) : GraphOperation;
