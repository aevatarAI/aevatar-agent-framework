using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

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
        // Use a factory method to choose the correct implementation based on context:
        // - In Silo (IGrainFactory available): Use SiloGAgentActorFactory for Grain-to-Grain calls
        // - In Client (only IClusterClient): Use OrleansGAgentActorFactory for remote calls
        services.AddSingleton<IGAgentActorFactory>(sp =>
        {
            var grainFactory = sp.GetService<IGrainFactory>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            
            if (grainFactory != null)
            {
                // Running in Silo - use SiloGAgentActorFactory for internal Grain-to-Grain calls
                return new SiloGAgentActorFactory(
                    grainFactory,
                    loggerFactory.CreateLogger<SiloGAgentActorFactory>());
            }
            else
            {
                // Running in Client - use OrleansGAgentActorFactory for remote calls
                var clusterClient = sp.GetRequiredService<IClusterClient>();
                var messageStreamProvider = sp.GetService<IMessageStreamProvider>();
                var providerOptions = sp.GetService<IOptions<MessageStreamProviderOptions>>();
                return new OrleansGAgentActorFactory(
                    sp,
                    clusterClient,
                    loggerFactory.CreateLogger<OrleansGAgentActorFactory>(),
                    messageStreamProvider,
                    providerOptions);
            }
        });
        
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        // MassTransit integration handlers (required for MassTransit stream routing)
        // These enable StreamMessageDispatcher to route events to Orleans Grains
        services.TryAddSingleton<IMassTransitEventHandler, OrleansMassTransitEventHandler>();
        services.TryAddSingleton<IStreamNotFoundHandler, OrleansStreamNotFoundHandler>();
        
        return services;
    }
}