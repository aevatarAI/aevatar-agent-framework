namespace Aevatar.Agents.Persistence.Graph.Core;

/// <summary>
/// 执行器接口：接收编译产物并执行，返回语义结果或 null。
/// <para>何时用：为不同后端实现执行逻辑（本库默认 Neo4jExecutor）。</para>
/// </summary>
public interface IGraphExecutor<TTarget>
{
    /// <summary>
    /// 执行编译后的命令。
    /// </summary>
    /// <param name="compiled">编译器输出的命令对象。</param>
    /// <returns>根据操作返回语义对象、标识，或对非查询写操作返回 null。</returns>
    /// <exception cref="Exception">后端执行失败时抛出。</exception>
    Task<object?> ExecuteAsync(TTarget compiled);
}
