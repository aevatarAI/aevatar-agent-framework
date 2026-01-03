using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.GAgent.Options;
using Aevatar.Agents.Persistence.Supabase.GAgent.Stores;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.DependencyInjection;

/// <summary>
/// Supabase(Postgres) persistence DI extensions for GAgent State/Config/EventRouter.
///
/// Design:
/// - Split from base infrastructure project: Aevatar.Agents.Persistence.Supabase (NpgsqlDataSource + shared helpers).
/// - This project focuses on stores + schema management for GAgent persistence.
/// </summary>
public static class SupabaseGAgentServiceCollectionExtensions
{
    /// <summary>
    /// Register Supabase(Postgres) for GAgent persistence (DataSource + Options).
    /// </summary>
    public static IServiceCollection AddAevatarSupabaseGAgent(
        this IServiceCollection services,
        string connectionString,
        Action<SupabasePersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        // Register NpgsqlDataSource (shared infra)
        services.AddAevatarSupabase(connectionString);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(o =>
            {
                o.ConnectionString = connectionString;
                configure?.Invoke(o);
            });

        return services;
    }

    /// <summary>
    /// Register Supabase(Postgres) options for GAgent persistence.
    /// Note: This overload does NOT register NpgsqlDataSource. Call services.AddAevatarSupabase(...) separately.
    /// </summary>
    public static IServiceCollection AddAevatarSupabaseGAgent(
        this IServiceCollection services,
        Action<SupabasePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(configure);

        return services;
    }

    /// <summary>
    /// Register Supabase StateStore (specific TState).
    /// Note: In the framework, the more common approach is:
    /// options.StateStoreType = typeof(SupabaseStateStore&lt;&gt;)
    /// </summary>
    public static IServiceCollection AddSupabaseStateStore<TState>(this IServiceCollection services)
        where TState : class, IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseStateStore<TState>>();
        return services;
    }

    /// <summary>
    /// Register Supabase ConfigStore (specific TConfig).
    /// </summary>
    public static IServiceCollection AddSupabaseConfigStore<TConfig>(this IServiceCollection services)
        where TConfig : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseConfigStore<TConfig>>();
        return services;
    }

    /// <summary>
    /// Register Supabase EventRouterStore.
    /// </summary>
    public static IServiceCollection AddSupabaseEventRouterStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseEventRouterStore>();
        return services;
    }
}


