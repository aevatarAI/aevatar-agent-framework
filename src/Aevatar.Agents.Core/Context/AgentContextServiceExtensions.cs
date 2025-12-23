using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Extension methods for registering agent context services.
/// </summary>
public static class AgentContextServiceExtensions
{
    /// <summary>
    /// Property key for storing runtime type in builder properties.
    /// </summary>
    public const string RuntimeTypeKey = "AgentRuntimeType";

    /// <summary>
    /// Adds agent context services using the default AsyncLocal implementation.
    /// By default, all keys are propagated (no allowlist restriction).
    /// </summary>
    public static IAevatarBuilder AddAgentContext(this IAevatarBuilder builder)
    {
        return builder.AddAgentContext(AgentContextPropagationOptions.Default);
    }

    /// <summary>
    /// Adds agent context services with custom propagation options.
    /// </summary>
    public static IAevatarBuilder AddAgentContext(
        this IAevatarBuilder builder,
        AgentContextPropagationOptions options)
    {
        builder.Services.TryAddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
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
    /// Adds agent context services with custom propagation options configured via delegate.
    /// </summary>
    public static IAevatarBuilder AddAgentContext(
        this IAevatarBuilder builder,
        Action<AgentContextPropagationOptions> configure)
    {
        var options = new AgentContextPropagationOptions();
        configure(options);
        return builder.AddAgentContext(options);
    }

    /// <summary>
    /// Adds agent context services with a specific accessor implementation.
    /// </summary>
    public static IAevatarBuilder AddAgentContext<TAccessor>(this IAevatarBuilder builder)
        where TAccessor : class, IAgentContextAccessor
    {
        return builder.AddAgentContext<TAccessor>(AgentContextPropagationOptions.Default);
    }

    /// <summary>
    /// Adds agent context services with a specific accessor and custom options.
    /// </summary>
    public static IAevatarBuilder AddAgentContext<TAccessor>(
        this IAevatarBuilder builder,
        AgentContextPropagationOptions options)
        where TAccessor : class, IAgentContextAccessor
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, TAccessor>());
        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IAgentContextAccessor>();
            var opts = sp.GetRequiredService<AgentContextPropagationOptions>();
            return new AgentContextPropagator(accessor, opts);
        });
        return builder;
    }
}

/// <summary>
/// Extension methods for IServiceCollection to add agent context services directly.
/// </summary>
public static class AgentContextServiceCollectionExtensions
{
    /// <summary>
    /// Adds agent context services using the default AsyncLocal implementation.
    /// </summary>
    public static IServiceCollection AddAgentContext(this IServiceCollection services)
    {
        return services.AddAgentContext(AgentContextPropagationOptions.Default);
    }

    /// <summary>
    /// Adds agent context services with custom propagation options.
    /// </summary>
    public static IServiceCollection AddAgentContext(
        this IServiceCollection services,
        AgentContextPropagationOptions options)
    {
        services.TryAddSingleton<IAgentContextAccessor, AsyncLocalAgentContextAccessor>();
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IAgentContextAccessor>();
            var opts = sp.GetRequiredService<AgentContextPropagationOptions>();
            return new AgentContextPropagator(accessor, opts);
        });
        return services;
    }

    /// <summary>
    /// Adds agent context services with custom propagation options via delegate.
    /// </summary>
    public static IServiceCollection AddAgentContext(
        this IServiceCollection services,
        Action<AgentContextPropagationOptions> configure)
    {
        var options = new AgentContextPropagationOptions();
        configure(options);
        return services.AddAgentContext(options);
    }

    /// <summary>
    /// Adds agent context services with a specific accessor implementation.
    /// </summary>
    public static IServiceCollection AddAgentContext<TAccessor>(this IServiceCollection services)
        where TAccessor : class, IAgentContextAccessor
    {
        services.Replace(ServiceDescriptor.Singleton<IAgentContextAccessor, TAccessor>());
        services.TryAddSingleton(AgentContextPropagationOptions.Default);
        services.TryAddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IAgentContextAccessor>();
            var opts = sp.GetRequiredService<AgentContextPropagationOptions>();
            return new AgentContextPropagator(accessor, opts);
        });
        return services;
    }
}

