using Aevatar.Agents.Persistence.SQLite.DependencyInjection;
using Aevatar.Agents.Persistence.SQLite.GAgent.Options;
using Aevatar.Agents.Persistence.SQLite.GAgent.Stores;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.DependencyInjection;

/// <summary>
/// SQLite persistence DI extensions for GAgent State/Config/EventRouter.
/// </summary>
public static class SQLiteGAgentServiceCollectionExtensions
{
    /// <summary>
    /// Register SQLite infrastructure + options for GAgent persistence.
    /// </summary>
    public static IServiceCollection AddAevatarSQLiteGAgent(
        this IServiceCollection services,
        string connectionString,
        Action<SQLitePersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        services.AddAevatarSQLite(connectionString);
        services.AddOptions<SQLitePersistenceOptions>()
            .Configure(o => configure?.Invoke(o));

        return services;
    }

    /// <summary>
    /// Register SQLite options for GAgent persistence (base infrastructure must be registered separately).
    /// </summary>
    public static IServiceCollection AddAevatarSQLiteGAgent(
        this IServiceCollection services,
        Action<SQLitePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SQLitePersistenceOptions>()
            .Configure(configure);

        return services;
    }

    /// <summary>
    /// Register SQLite StateStore (specific TState).
    /// </summary>
    public static IServiceCollection AddSQLiteStateStore<TState>(this IServiceCollection services)
        where TState : class, IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SQLitePersistenceOptions>();
        services.AddSingleton<SQLiteStateStore<TState>>();
        return services;
    }

    /// <summary>
    /// Register SQLite ConfigStore (specific TConfig).
    /// </summary>
    public static IServiceCollection AddSQLiteConfigStore<TConfig>(this IServiceCollection services)
        where TConfig : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SQLitePersistenceOptions>();
        services.AddSingleton<SQLiteConfigStore<TConfig>>();
        return services;
    }

    /// <summary>
    /// Register SQLite EventRouterStore.
    /// </summary>
    public static IServiceCollection AddSQLiteEventRouterStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SQLitePersistenceOptions>();
        services.AddSingleton<SQLiteEventRouterStore>();
        return services;
    }
}
