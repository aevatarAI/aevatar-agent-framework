using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Persistence.Neo4j;
using Aevatar.Agents.Persistence.Neo4j.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Persistence.Neo4j.MemoryGraph.DependencyInjection;

// ============================================================
//  Neo4j MemoryGraph DI Extensions
//
//  Design:
//  - Reuse Neo4j infrastructure from Aevatar.Agents.Persistence.Neo4j
//    (driver/session/client/options).
//  - Register IMemoryGraphStore implementation.
// ============================================================
public static class Neo4jMemoryGraphServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Neo4j MemoryGraphStore（便捷重载）。
    /// <para>注意：该方法会复用/注册 Neo4j Driver 基础设施。</para>
    /// </summary>
    public static IServiceCollection AddAevatarMemoryGraphNeo4j(
        this IServiceCollection services,
        string uri,
        string username,
        string password,
        string database = "neo4j")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAevatarNeo4j(uri, username, password, database);
        services.TryAddSingleton<IMemoryGraphStore, Neo4jMemoryGraphStore>();
        return services;
    }

    /// <summary>
    /// 注册 Neo4j MemoryGraphStore（高级配置）。
    /// </summary>
    public static IServiceCollection AddAevatarMemoryGraphNeo4j(
        this IServiceCollection services,
        Action<Neo4jPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddAevatarNeo4j(configure);
        services.TryAddSingleton<IMemoryGraphStore, Neo4jMemoryGraphStore>();
        return services;
    }

    /// <summary>
    /// 强制替换 IMemoryGraphStore 为 Neo4j 版本（不依赖调用顺序）。
    /// </summary>
    public static IServiceCollection ReplaceAevatarMemoryGraphStoreWithNeo4j(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IMemoryGraphStore, Neo4jMemoryGraphStore>());
        return services;
    }
}


