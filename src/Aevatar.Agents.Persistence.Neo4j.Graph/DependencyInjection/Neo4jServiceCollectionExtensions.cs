using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Neo4j;
using Aevatar.Agents.Persistence.Neo4j.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;

/// <summary>
/// Neo4j 接入的 DI 扩展。
/// </summary>
public static class Neo4jServiceCollectionExtensions
{
    /// <summary>
    /// 从环境变量读取配置并注册 Neo4j Graph（推荐用于生产环境）。
    /// <para>支持的环境变量：NEO4J_URI, NEO4J_USERNAME, NEO4J_PASSWORD, NEO4J_DATABASE</para>
    /// </summary>
    public static IServiceCollection AddAevatarGraphNeo4j(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Base Neo4j infra (driver/session/client) - reads from env vars
        services.AddAevatarNeo4j();

        // Graph provider (compiler/executor/client facade)
        services.TryAddSingleton<IGraphCompiler<CypherCommand>, CypherCompiler>();
        services.TryAddSingleton<IGraphExecutor<CypherCommand>, Neo4jExecutor>();
        services.TryAddSingleton<IGraphClient, GraphClient<CypherCommand>>();

        return services;
    }

    /// <summary>
    /// 使用字符串参数注册 Neo4j（便捷重载）。
    /// </summary>
    /// <param name="services">DI 容器，必非 null。</param>
    /// <param name="uri">Neo4j 连接 URI（bolt/neo4j+s）。</param>
    /// <param name="username">用户名。</param>
    /// <param name="password">密码。</param>
    /// <param name="database">数据库名，默认 neo4j。</param>
    /// <returns>同一个 <see cref="IServiceCollection"/> 便于链式调用。</returns>
    public static IServiceCollection AddAevatarGraphNeo4j(
        this IServiceCollection services,
        string uri,
        string username,
        string password,
        string database = "neo4j") =>
        services.AddAevatarGraphNeo4j(options =>
        {
            options.Uri = uri;
            options.Username = username;
            options.Password = password;
            options.Database = database;
        });

    /// <summary>
    /// 使用委托配置 Neo4j 选项并注册 Driver/Session/Client。
    /// </summary>
    /// <param name="services">DI 容器，必非 null。</param>
    /// <param name="configure">配置回调，需填写 Uri/Username/Password（database 可选）。</param>
    /// <returns>同一个 <see cref="IServiceCollection"/>。</returns>
    /// <exception cref="ArgumentNullException">services 或 configure 为 null。</exception>
    /// <exception cref="Options.ValidationException">选项校验失败。</exception>
    public static IServiceCollection AddAevatarGraphNeo4j(
        this IServiceCollection services,
        Action<Neo4jPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // 1) Base Neo4j infra (driver/session/client)
        services.AddAevatarNeo4j(configure);

        // 2) Graph provider (compiler/executor/client facade)
        services.TryAddSingleton<IGraphCompiler<CypherCommand>, CypherCompiler>();
        services.TryAddSingleton<IGraphExecutor<CypherCommand>, Neo4jExecutor>();
        services.TryAddSingleton<IGraphClient, GraphClient<CypherCommand>>();

        return services;
    }
}
