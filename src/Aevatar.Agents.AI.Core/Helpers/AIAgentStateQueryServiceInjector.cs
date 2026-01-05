using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Automatically injects CQRS <see cref="IStateQueryService"/> into AI agents (AIGAgentBase).
/// <para/>
/// WHY:
/// - Built-in tools (e.g. search_memory) should be able to query the projected read-model (Elasticsearch)
///   without depending on app-layer services.
/// </summary>
public static class AIAgentStateQueryServiceInjector
{
    public static void InjectStateQueryService(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindStateQueryServiceProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var service = serviceProvider.GetService(typeof(IStateQueryService)) as IStateQueryService;
        if (service == null)
            return;

        try
        {
            // Best-effort: only set when null to avoid overriding custom wiring.
            if (property.GetValue(agent) != null)
                return;

            property.SetValue(agent, service);
        }
        catch
        {
            // Best-effort injection; do not break agent creation pipeline.
        }
    }

    private static PropertyInfo? FindStateQueryServiceProperty(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.Name == "CqrsStateQueryService" &&
                typeof(IStateQueryService).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }
        }

        return type.BaseType != null && type.BaseType != typeof(object)
            ? FindStateQueryServiceProperty(type.BaseType)
            : null;
    }
}


