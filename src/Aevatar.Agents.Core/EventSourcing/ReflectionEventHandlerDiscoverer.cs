using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Default implementation of IEventHandlerDiscoverer using reflection.
/// </summary>
public class ReflectionEventHandlerDiscoverer : IEventHandlerDiscoverer
{
    public MethodInfo[] DiscoverEventHandlers(Type type)
    {
        var selected = new Dictionary<MethodInfo, MethodInfo>();
        var current = type;

        while (current != null)
        {
            var methods = current.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                if (!IsEventHandlerMethod(method))
                    continue;

                var baseDefinition = method.GetBaseDefinition();
                if (selected.ContainsKey(baseDefinition))
                    continue;

                selected[baseDefinition] = method;
            }

            current = current.BaseType;
        }

        return selected.Values
            .OrderBy(m => m.GetCustomAttribute<EventHandlerAttribute>()?.Priority ??
                          m.GetCustomAttribute<AllEventHandlerAttribute>()?.Priority ??
                          int.MaxValue)
            .ToArray();
    }

    /// <summary>
    /// Determine if a method is an event handler.
    /// </summary>
    protected virtual bool IsEventHandlerMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;

        var paramType = parameters[0].ParameterType;

        // [EventHandler] marked methods, parameter must be IMessage
        if (method.GetCustomAttribute<EventHandlerAttribute>() != null)
        {
            return typeof(IMessage).IsAssignableFrom(paramType);
        }

        // [AllEventHandler] marked methods, parameter must be EventEnvelope
        if (method.GetCustomAttribute<AllEventHandlerAttribute>() != null)
        {
            return paramType == typeof(EventEnvelope);
        }

        // Convention-based handlers: method named HandleAsync or HandleEventAsync, parameter is IMessage
        if (method.Name is "HandleAsync" or "HandleEventAsync")
        {
            return typeof(IMessage).IsAssignableFrom(paramType) && !paramType.IsAbstract;
        }

        return false;
    }
}
