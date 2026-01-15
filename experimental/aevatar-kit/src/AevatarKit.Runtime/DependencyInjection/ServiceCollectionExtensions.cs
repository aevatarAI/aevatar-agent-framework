using Microsoft.Extensions.DependencyInjection;
using AevatarKit.Runtime.Run;
using AevatarKit.Runtime.Agents;
using AevatarKit.Runtime.Graphs;
using AevatarKit.Runtime.Mcp;
using AevatarKit.Runtime.Memory;

namespace AevatarKit.Runtime.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarKitRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRunRegistry, InMemoryRunRegistry>();
        services.AddSingleton<IMemoryStore, InMemoryMemoryStore>();
        services.AddSingleton<IRunEngine, MvpRunEngine>();
        services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
        services.AddSingleton<IGraphRegistry, InMemoryGraphRegistry>();
        services.AddSingleton<IMcpServerRegistry, InMemoryMcpServerRegistry>();

        return services;
    }
}


