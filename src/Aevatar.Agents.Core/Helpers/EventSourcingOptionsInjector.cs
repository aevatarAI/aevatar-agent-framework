using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Injects EventSourcingOptions into Agent instances
/// </summary>
public static class EventSourcingOptionsInjector
{
    /// <summary>
    /// Inject EventSourcingOptions into Agent
    /// </summary>
    public static void InjectEventSourcingOptions(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var options = serviceProvider.GetService<IOptions<EventSourcingOptions>>()?.Value;
        if (options == null)
            return;

        var agentType = agent.GetType();
        var property = FindEventSourcingOptionsProperty(agentType);
        
        if (property != null && property.CanWrite)
        {
            try
            {
                property.SetValue(agent, options);
            }
            catch
            {
                // Silently ignore injection failures
            }
        }
    }

    private static PropertyInfo? FindEventSourcingOptionsProperty(Type agentType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        var type = agentType;
        while (type != null && type != typeof(object))
        {
            var property = type.GetProperty("EventSourcingOptions", bindingFlags);
            if (property != null && property.PropertyType == typeof(EventSourcingOptions))
            {
                return property;
            }
            type = type.BaseType;
        }

        return null;
    }
}

