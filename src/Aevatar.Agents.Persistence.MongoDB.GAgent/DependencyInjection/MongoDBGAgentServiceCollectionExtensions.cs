using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB.GAgent.DependencyInjection;

/// <summary>
/// DI extension methods for MongoDB GAgent persistence (State/Config/EventRouter).
/// Requires base infra services.AddAevatarMongoDB(...) to be called first.
/// </summary>
public static class MongoDBGAgentServiceCollectionExtensions
{
    /// <summary>
    /// Add MongoDB state store for a specific state type.
    /// Requires services.AddAevatarMongoDB(...) to be called first.
    /// </summary>
    public static IServiceCollection AddMongoDBStateStore<TState>(
        this IServiceCollection services,
        string? collectionName = null)
        where TState : class, Google.Protobuf.IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDBStateStore<TState>(database, collectionName);
        });

        return services;
    }

    /// <summary>
    /// Add MongoDB config store for a specific config type.
    /// Requires services.AddAevatarMongoDB(...) to be called first.
    /// </summary>
    public static IServiceCollection AddMongoDBConfigStore<TConfig>(
        this IServiceCollection services,
        string? collectionName = null)
        where TConfig : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDbConfigStore<TConfig>(database, collectionName);
        });

        return services;
    }

    /// <summary>
    /// Add MongoDB EventRouter store.
    /// Requires services.AddAevatarMongoDB(...) to be called first.
    /// </summary>
    public static IServiceCollection AddMongoDBEventRouterStore(
        this IServiceCollection services,
        string? collectionName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var database = sp.GetRequiredService<IMongoDatabase>();
            return new MongoDBEventRouterStore(database, collectionName);
        });

        return services;
    }
}


