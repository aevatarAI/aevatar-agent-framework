using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Extension methods for registering Orleans-specific agent context services.
/// </summary>
public static class OrleansAgentContextExtensions
{
    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge.
    /// </summary>
    public static IAevatarBuilder AddOrleansAgentContext(this IAevatarBuilder builder)
    {
        return builder.AddOrleansAgentContext(AgentContextPropagationOptions.Default);
    }

    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge with custom options.
    /// </summary>
    public static IAevatarBuilder AddOrleansAgentContext(
        this IAevatarBuilder builder,
        AgentContextPropagationOptions options)
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, OrleansAgentContextAccessor>());
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IAgentContextAccessor>();
            var opts = sp.GetRequiredService<AgentContextPropagationOptions>();
            return new AgentContextPropagator(accessor, opts);
        });
        return builder;
    }

    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge with options delegate.
    /// </summary>
    public static IAevatarBuilder AddOrleansAgentContext(
        this IAevatarBuilder builder,
        Action<AgentContextPropagationOptions> configure)
    {
        var options = new AgentContextPropagationOptions();
        configure(options);
        return builder.AddOrleansAgentContext(options);
    }

    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge.
    /// </summary>
    public static IServiceCollection AddOrleansAgentContext(this IServiceCollection services)
    {
        return services.AddOrleansAgentContext(AgentContextPropagationOptions.Default);
    }

    /// <summary>
    /// Adds agent context services using the Orleans RequestContext bridge with custom options.
    /// </summary>
    public static IServiceCollection AddOrleansAgentContext(
        this IServiceCollection services,
        AgentContextPropagationOptions options)
    {
        services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, OrleansAgentContextAccessor>());
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IAgentContextAccessor>();
            var opts = sp.GetRequiredService<AgentContextPropagationOptions>();
            return new AgentContextPropagator(accessor, opts);
        });
        return services;
    }
}

