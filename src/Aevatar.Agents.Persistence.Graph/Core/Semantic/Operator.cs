namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

/// <summary>
/// 查询比较操作符，编译为对应的 Cypher 比较。
/// </summary>
public enum Operator
{
    Equals,
    GreaterThan,
    LessThan,
    Contains
}
