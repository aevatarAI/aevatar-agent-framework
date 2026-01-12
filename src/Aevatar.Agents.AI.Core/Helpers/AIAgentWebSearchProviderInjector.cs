using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Best-effort injection of <see cref="IAevatarWebSearchProvider"/> into AI agents that support web search.
/// <para/>
/// Convention:
/// - Agent has a writable property named <c>WebSearchProvider</c>.
/// </summary>
public static class AIAgentWebSearchProviderInjector
{
    public static void InjectWebSearchProvider(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindWebSearchProviderProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var provider = serviceProvider.GetService(property.PropertyType);
        if (provider == null)
            return;

        try
        {
            property.SetValue(agent, provider);
        }
        catch
        {
            // Best-effort: never fail agent creation due to injection.
        }
    }

    private static PropertyInfo? FindWebSearchProviderProperty(Type agentType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                "WebSearchProvider",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && typeof(IAevatarWebSearchProvider).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}


