using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Graph.Providers.Neo4j;

/// <summary>
/// Neo4j 接入的 DI 扩展。
/// </summary>
public static class Neo4jServiceCollectionExtensions
{
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

        services
            .AddOptions<Neo4jPersistenceOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Uri), "Neo4j Uri is required.")
            .Validate(o => Uri.IsWellFormedUriString(o.Uri, UriKind.Absolute), "Neo4j Uri is invalid.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "Neo4j Username is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "Neo4j Password is required.");

        services.TryAddSingleton<INeo4jDriverFactory, Neo4jDriverFactory>();

        services.TryAddSingleton<IDriver>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Neo4jPersistenceOptions>>().Value;
            var factory = sp.GetRequiredService<INeo4jDriverFactory>();
            return factory.Create(options);
        });

        services.TryAddSingleton<INeo4jSessionFactory, Neo4jSessionFactory>();
        services.TryAddSingleton<INeo4jClient, Neo4jClient>();
        services.TryAddSingleton<IGraphCompiler<CypherCommand>, CypherCompiler>();
        services.TryAddSingleton<IGraphExecutor<CypherCommand>, Neo4jExecutor>();
        services.TryAddSingleton<IGraphClient, GraphClient>();

        return services;
    }
}
