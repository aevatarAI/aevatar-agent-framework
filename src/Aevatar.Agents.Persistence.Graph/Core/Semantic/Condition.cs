namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

/// <summary>
/// 查询条件：指定属性、操作符与期望值。
/// <para>属性名需与存储属性一致；值类型需与属性类型匹配。</para>
/// </summary>
/// <param name="Property">属性名。</param>
/// <param name="Operator">比较操作符。</param>
/// <param name="Value">比较值，使用 <see cref="Value"/> 包装。</param>
public sealed record Condition(
    string Property,
    Operator Operator,
    Value Value
);
