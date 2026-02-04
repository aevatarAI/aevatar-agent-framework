using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Runtime.Local.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Extension methods for configuring the Local agent runtime in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Local agent runtime core services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAevatarLocalRuntime(this IServiceCollection services)
    {
        // Register factory
        services.TryAddSingleton<LocalGAgentActorFactory>();
        services.TryAddSingleton<IGAgentActorFactory>(provider =>
            provider.GetRequiredService<LocalGAgentActorFactory>());

        // Register manager
        services.TryAddSingleton<IGAgentActorManager, LocalGAgentActorManager>();
        
        // Register Local-specific components
        services.TryAddSingleton<LocalMessageStreamRegistry>();
        services.TryAddSingleton<LocalSubscriptionManager>();
        services.TryAddSingleton<LocalMessageStreamProvider>();
        services.TryAddSingleton<IMessageStreamProvider>(sp => sp.GetRequiredService<LocalMessageStreamProvider>());

        // Register Handler required for MassTransit support (even if it just throws exception)
        services.TryAddSingleton<IStreamNotFoundHandler, LocalStreamNotFoundHandler>();

        // Register default factory provider (if not registered)
        services.TryAddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();
        services.TryAddSingleton<IGAgentFactory, DefaultGAgentFactory>();

        return services;
    }
}
