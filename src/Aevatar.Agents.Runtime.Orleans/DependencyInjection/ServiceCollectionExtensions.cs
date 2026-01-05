using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Orleans Agent Actor runtime support
    /// </summary>
    public static IServiceCollection AddOrleansAgentRuntime(this IServiceCollection services)
    {
        // Register factory
        services.TryAddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.TryAddSingleton<OrleansGAgentActorFactory>(); // Also register concrete type, just in case

        // Register manager
        services.TryAddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        
        // Register Stream Not Found Handler
        services.AddSingleton<IStreamNotFoundHandler, OrleansStreamNotFoundHandler>();

        // Register Stream Factory (unified Stream creation)
        services.TryAddSingleton<OrleansStreamFactory>();

        return services;
    }
}

