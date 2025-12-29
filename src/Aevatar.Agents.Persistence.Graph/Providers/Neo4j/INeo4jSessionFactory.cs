using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 统一的 Session 创建/释放入口，确保连接可被替换和测试。
/// </summary>
public interface INeo4jSessionFactory
{
    /// <summary>
    /// 创建只读 Session 并在其内执行工作委托。
    /// 约束：work 非 null；调用方负责处理中取消。
    /// 返回：work 的执行结果。
    /// 异常：work 抛出或驱动创建/执行失败时抛出。
    /// </summary>
    Task<T> ExecuteReadAsync<T>(Func<IAsyncSession, Task<T>> work, CancellationToken ct = default);

    /// <summary>
    /// 创建写 Session 并在其内执行工作委托。
    /// 约束：work 非 null；调用方负责处理中取消。
    /// 返回：work 的执行结果。
    /// 异常：work 抛出或驱动创建/执行失败时抛出。
    /// </summary>
    Task<T> ExecuteWriteAsync<T>(Func<IAsyncSession, Task<T>> work, CancellationToken ct = default);
}
