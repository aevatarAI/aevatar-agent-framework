using System.Reflection;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Http;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Best-effort injection of <see cref="IHttpClientFactory"/> into AI agents.
/// <para/>
/// Convention:
/// - Agent has a writable property named <c>HttpClientFactory</c>.
/// </summary>
public static class AIAgentHttpClientFactoryInjector
{
    public static void InjectHttpClientFactory(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindHttpClientFactoryProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var factory = serviceProvider.GetService(typeof(IHttpClientFactory));
        if (factory == null)
            return;

        try
        {
            property.SetValue(agent, factory);
        }
        catch
        {
            // Best-effort: never fail agent creation due to injection.
        }
    }

    private static PropertyInfo? FindHttpClientFactoryProperty(Type agentType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                "HttpClientFactory",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && typeof(IHttpClientFactory).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}


