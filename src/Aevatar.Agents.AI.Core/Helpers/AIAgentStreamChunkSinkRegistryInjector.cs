using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Best-effort injection for <see cref="IStreamChunkSinkRegistry"/> into AI agents.
/// </summary>
public static class AIAgentStreamChunkSinkRegistryInjector
{
    public static void InjectRegistry(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindRegistryProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var registry = serviceProvider.GetService(property.PropertyType);
        if (registry == null)
            return;

        try
        {
            property.SetValue(agent, registry);
        }
        catch
        {
            // Best-effort: never fail agent creation due to optional deps.
        }
    }

    private static PropertyInfo? FindRegistryProperty(Type agentType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                "StreamChunkSinkRegistry",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && typeof(IStreamChunkSinkRegistry).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}

