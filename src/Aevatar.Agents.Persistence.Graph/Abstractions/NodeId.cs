namespace Aevatar.Agents.Persistence.Graph.Abstractions;

/// <summary>
/// Strongly-typed identifier for graph nodes。
/// 用途：所有节点相关 API 的 id 参数/返回值，避免裸字符串。
/// 约束：Value 应为非空、可被底层存储匹配的字符串。
/// 返回/异常：值类型本身不为 null；若 Value 为空，可能导致查询不到节点。
/// </summary>
/// <param name="Value">Underlying node id string.</param>
public readonly record struct NodeId(string Value);
