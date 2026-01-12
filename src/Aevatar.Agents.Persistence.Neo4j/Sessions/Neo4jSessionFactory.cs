using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Neo4j;

/// <summary>
/// 管理 Neo4j Session 生命周期的默认实现。
/// </summary>
public sealed class Neo4jSessionFactory : INeo4jSessionFactory
{
    private readonly IDriver _driver;
    private readonly Neo4jPersistenceOptions _options;

    /// <summary>
    /// 注入 Driver 与连接选项。
    /// <param name="driver">Neo4j 驱动实例，必非 null。</param>
    /// <param name="options">连接配置（包含数据库名），必非 null。</param>
    /// </summary>
    public Neo4jSessionFactory(IDriver driver, IOptions<Neo4jPersistenceOptions> options)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 创建只读 Session 并执行工作委托。
    /// 约束：work 非 null；使用 Read 访问模式与配置的 Database。
    /// 返回：work 结果。
    /// 异常：work 抛出或驱动失败时抛出。
    /// </summary>
    public Task<T> ExecuteReadAsync<T>(Func<IAsyncSession, Task<T>> work, CancellationToken ct = default) =>
        ExecuteAsync(AccessMode.Read, work, ct);

    /// <summary>
    /// 创建写 Session 并执行工作委托。
    /// 约束：work 非 null；使用 Write 访问模式与配置的 Database。
    /// 返回：work 结果。
    /// 异常：work 抛出或驱动失败时抛出。
    /// </summary>
    public Task<T> ExecuteWriteAsync<T>(Func<IAsyncSession, Task<T>> work, CancellationToken ct = default) =>
        ExecuteAsync(AccessMode.Write, work, ct);

    private async Task<T> ExecuteAsync<T>(AccessMode mode, Func<IAsyncSession, Task<T>> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(work);

        await using var session = _driver.AsyncSession(o => ConfigureSession(o, mode));
        return await work(session);
    }

    private void ConfigureSession(SessionConfigBuilder builder, AccessMode mode)
    {
        builder.WithDefaultAccessMode(mode);

        if (!string.IsNullOrWhiteSpace(_options.Database))
        {
            builder.WithDatabase(_options.Database);
        }
    }
}
