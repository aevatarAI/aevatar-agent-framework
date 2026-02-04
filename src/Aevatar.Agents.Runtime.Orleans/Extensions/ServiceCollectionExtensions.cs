using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Aevatar Agent System using Orleans Runtime.
    /// Pre-requisite: You must configure Orleans Host (silo/client) separately.
    /// </summary>
    public static IServiceCollection AddAevatarOrleansRuntime(this IServiceCollection services)
    {
        // Core Orleans runtime services
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        services.TryAddSingleton<IGAgentFactory, DefaultGAgentFactory>();
        
        // MassTransit integration handlers (required for MassTransit stream routing)
        // These enable StreamMessageDispatcher to route events to Orleans Grains
        services.TryAddSingleton<IMassTransitEventHandler, OrleansMassTransitEventHandler>();
        services.TryAddSingleton<IStreamNotFoundHandler, OrleansStreamNotFoundHandler>();
        
        return services;
    }
}