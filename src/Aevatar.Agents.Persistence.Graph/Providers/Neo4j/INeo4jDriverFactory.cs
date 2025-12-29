using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// 创建 Neo4j <see cref="IDriver" /> 的抽象，便于单元测试注入替身。
/// </summary>
public interface INeo4jDriverFactory
{
    /// <summary>
    /// 创建 Neo4j <see cref="IDriver" /> 实例，供上层 Session 工厂使用。
    /// </summary>
    /// <param name="options">连接配置（Uri/用户名/密码/数据库）。</param>
    /// <returns>配置好的 <see cref="IDriver"/> 实例。</returns>
    /// <exception cref="ArgumentException">配置缺失或格式无效时抛出。</exception>
    IDriver Create(Neo4jPersistenceOptions options);
}
