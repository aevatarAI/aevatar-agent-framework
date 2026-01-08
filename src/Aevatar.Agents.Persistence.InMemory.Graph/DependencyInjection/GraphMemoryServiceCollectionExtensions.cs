using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// DI extensions for InMemory Graph Provider.
/// </summary>
public static class GraphInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// Register InMemory Graph provider (fastest for dev/test, no external dependencies).
    /// </summary>
    public static IServiceCollection AddAevatarGraphInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryGraphStore>();
        services.TryAddSingleton<IGraphCompiler<GraphOperation>, PassThroughGraphCompiler>();
        services.TryAddSingleton<IGraphExecutor<GraphOperation>, InMemoryGraphExecutor>();
        services.TryAddSingleton<IGraphClient, GraphClient<GraphOperation>>();

        return services;
    }

    /// <summary>
    /// Compatibility naming: legacy usage/terminology for Graph.InMemory (still registers InMemory provider).
    /// </summary>
    public static IServiceCollection AddAevatarGraphMemory(this IServiceCollection services)
        => services.AddAevatarGraphInMemory();
}


