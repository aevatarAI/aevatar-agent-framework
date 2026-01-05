namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

/// <summary>
/// 关系查询条件：可选 Type 与属性条件列表。
/// </summary>
public sealed class EdgeQuery
{
    /// <summary>
    /// 关系类型/标签；为空时匹配任意类型。
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 属性条件列表；属性名需与存储中的关系属性一致。
    /// </summary>
    public IReadOnlyList<Condition> Conditions { get; init; } = [];
}
