using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Helper class to inject IAgentContextAccessor into GAgentBase and AgentContextPropagator into GAgentActorBase.
/// </summary>
public static class AgentContextAccessorInjector
{
    /// <summary>
    /// Inject context accessor into a GAgentBase instance.
    /// </summary>
    public static void InjectContextAccessor(IGAgent agent, IAgentContextAccessor contextAccessor)
    {
        if (agent is GAgentBase gAgentBase)
        {
            gAgentBase.ContextAccessor = contextAccessor;
        }
    }

    /// <summary>
    /// Inject context propagator into a GAgentActorBase instance.
    /// Uses propagator from DI if available, otherwise creates one with default options.
    /// </summary>
    public static void InjectContextPropagator(
        GAgentActorBase actor,
        IServiceProvider serviceProvider)
    {
        // Try to get propagator from DI (with configured options)
        var propagator = serviceProvider.GetService<AgentContextPropagator>();
        if (propagator != null)
        {
            actor.ContextPropagator = propagator;
            return;
        }

        // Fallback: create with default options if accessor is available
        var accessor = serviceProvider.GetService<IAgentContextAccessor>();
        if (accessor != null)
        {
            actor.ContextPropagator = new AgentContextPropagator(accessor);
        }
    }

    /// <summary>
    /// Inject context accessor and propagator from service provider.
    /// </summary>
    public static void InjectContext(
        IGAgent agent,
        GAgentActorBase actor,
        IServiceProvider serviceProvider)
    {
        var accessor = serviceProvider.GetService<IAgentContextAccessor>();
        if (accessor != null)
        {
            InjectContextAccessor(agent, accessor);
        }
        InjectContextPropagator(actor, serviceProvider);
    }
}

