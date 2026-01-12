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
    /// Register Neo4j MemoryGraphStore (convenience overload).
    /// <para>Note: This method will reuse/register Neo4j Driver infrastructure.</para>
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
    /// Register Neo4j MemoryGraphStore (advanced configuration).
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
    /// Force replace IMemoryGraphStore with Neo4j version (independent of call order).
    /// </summary>
    public static IServiceCollection ReplaceAevatarMemoryGraphStoreWithNeo4j(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IMemoryGraphStore, Neo4jMemoryGraphStore>());
        return services;
    }
}


