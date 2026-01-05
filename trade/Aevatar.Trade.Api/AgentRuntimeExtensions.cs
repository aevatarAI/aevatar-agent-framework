using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.Subscription;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Runtime.Orleans.Subscription;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Trade.Api;

/// <summary>
/// Agent runtime extensions
/// </summary>
public static class AgentRuntimeExtensions
{
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runtimeOptions = configuration
            .GetSection(AgentRuntimeOptions.SectionName)
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        // Event Store (Event Sourcing)
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        // Event Deduplicator
        services.AddSingleton<IEventDeduplicator>(sp =>
            new MemoryCacheEventDeduplicator(new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 10_000,
                EnableAutoCleanup = true
            }));

        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                ConfigureLocalRuntime(services);
                break;

            case AgentRuntimeType.Orleans:
                ConfigureOrleansRuntime(services);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }

    private static void ConfigureLocalRuntime(IServiceCollection services)
    {
        // Align with framework runtime DI:
        // - Registers IGAgentActorFactory/Manager
        // - Registers IGAgentFactory (AIGAgentFactory) which is REQUIRED for agent creation
        // - Registers LocalMessageStreamRegistry + LocalSubscriptionManager
        services.AddAevatarLocalRuntime();

        // The runtime registers LocalSubscriptionManager as concrete type; expose it via interface for callers.
        services.TryAddSingleton<ISubscriptionManager>(sp => sp.GetRequiredService<LocalSubscriptionManager>());

        Console.WriteLine("✅ Using Local runtime (single-machine in-memory mode)");
    }

    private static void ConfigureOrleansRuntime(IServiceCollection services)
    {
        // Align with framework runtime DI:
        // - Registers IGAgentActorFactory/Manager
        // - Registers IGAgentFactory (AIGAgentFactory) which is REQUIRED for agent creation
        services.AddAevatarOrleansRuntime();

        services.AddSingleton<ISubscriptionManager>(sp =>
        {
            var client = sp.GetRequiredService<Orleans.IClusterClient>();
            var streamProvider = client.GetStreamProvider("DefaultStreamProvider");
            return new OrleansSubscriptionManager(
                streamProvider,
                AevatarAgentsOrleansConstants.StreamNamespace,
                sp.GetRequiredService<ILogger<OrleansSubscriptionManager>>());
        });

        Console.WriteLine("✅ Using Orleans runtime (distributed mode)");
    }
}
