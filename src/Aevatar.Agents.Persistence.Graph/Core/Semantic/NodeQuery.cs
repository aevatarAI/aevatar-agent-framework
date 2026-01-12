namespace Aevatar.Agents.Persistence.Graph.Core.Semantic;

/// <summary>
/// 节点查询条件：必填 Type，可附带属性条件列表。
/// </summary>
public sealed class NodeQuery
{
    /// <summary>
    /// 节点类型/标签，必填，用于 MATCH (n:`Type`)。
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 可选属性条件列表；属性名需与存储中的属性一致。
    /// </summary>
    public IReadOnlyList<Condition> Conditions { get; init; } = [];
}
