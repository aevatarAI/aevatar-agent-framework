using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Framework dependency injection extensions
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Add Orleans Agent support
    /// </summary>
    public static IServiceCollection AddOrleansActorFactory(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();
        return services;
    }
}
