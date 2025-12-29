namespace Aevatar.Agents.Persistence.Graph.Core.IR;

/// <summary>
/// 编译入口：封装一个 <see cref="GraphOperation"/>，供运行时编译到目标命令。
/// </summary>
public sealed class GraphPlan
{
    /// <summary>
    /// 要编译/执行的图操作，不能为空。
    /// </summary>
    public required GraphOperation Operation { get; init; }
}
