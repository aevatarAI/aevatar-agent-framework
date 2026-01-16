using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Aevatar.Agents.Persistence.Neo4j.DependencyInjection;

/// <summary>
/// Neo4j 基础设施 DI 扩展（Driver/Session/Client）。
/// <para>不包含任何"业务能力"（例如 Graph 编译/执行）。</para>
/// </summary>
public static class Neo4jServiceCollectionExtensions
{
    /// <summary>
    /// 从环境变量读取配置并注册 Neo4j（推荐用于生产环境）。
    /// <para>支持的环境变量：NEO4J_URI, NEO4J_USERNAME, NEO4J_PASSWORD, NEO4J_DATABASE</para>
    /// </summary>
    public static IServiceCollection AddAevatarNeo4j(this IServiceCollection services) =>
        services.AddAevatarNeo4j(options =>
        {
            options.Uri = Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687";
            options.Username = Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j";
            options.Password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? string.Empty;
            options.Database = Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "neo4j";
        });

    /// <summary>
    /// 便捷注册：Neo4j Driver + SessionFactory + Neo4jClient。
    /// </summary>
    public static IServiceCollection AddAevatarNeo4j(
        this IServiceCollection services,
        string uri,
        string username,
        string password,
        string database = "neo4j") =>
        services.AddAevatarNeo4j(options =>
        {
            options.Uri = uri;
            options.Username = username;
            options.Password = password;
            options.Database = database;
        });

    /// <summary>
    /// 高级注册：通过委托配置 <see cref="Neo4jPersistenceOptions"/>。
    /// </summary>
    public static IServiceCollection AddAevatarNeo4j(
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

        return services;
    }
}


