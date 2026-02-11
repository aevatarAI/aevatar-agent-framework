using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Default implementation of IEventHandlerDiscoverer using reflection.
/// Walks the inheritance chain with DeclaredOnly to ensure proper deduplication
/// when derived classes override or hide base handlers.
/// </summary>
public class ReflectionEventHandlerDiscoverer : IEventHandlerDiscoverer
{
    public MethodInfo[] DiscoverEventHandlers(Type type)
    {
        // Collect unique handlers by walking the inheritance chain bottom-up.
        // This ensures that if a derived class overrides or hides a base handler,
        // only the most-derived version is included.
        var seen = new HashSet<MethodInfo>(MethodBaseDefinitionComparer.Instance);
        var handlers = new List<(MethodInfo Method, int Priority)>();

        var current = type;
        while (current != null && current != typeof(object))
        {
            var declaredMethods = current.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly);

            foreach (var method in declaredMethods)
            {
                if (!IsEventHandlerMethod(method)) continue;

                // Dedup: only keep the most-derived version
                if (!seen.Add(method)) continue;

                var priority = method.GetCustomAttribute<EventHandlerAttribute>()?.Priority
                               ?? method.GetCustomAttribute<AllEventHandlerAttribute>()?.Priority
                               ?? int.MaxValue;

                handlers.Add((method, priority));
            }

            current = current.BaseType;
        }

        return handlers
            .OrderBy(h => h.Priority)
            .Select(h => h.Method)
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

        // Convention-based handlers: HandleAsync or HandleEventAsync
        if (method.Name is "HandleAsync" or "HandleEventAsync")
        {
            return typeof(IMessage).IsAssignableFrom(paramType) && !paramType.IsAbstract;
        }

        return false;
    }

    /// <summary>
    /// Comparer that treats two MethodInfo instances as equal
    /// if they resolve to the same base definition (virtual slot).
    /// </summary>
    private sealed class MethodBaseDefinitionComparer : IEqualityComparer<MethodInfo>
    {
        public static readonly MethodBaseDefinitionComparer Instance = new();

        public bool Equals(MethodInfo? x, MethodInfo? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.GetBaseDefinition().Equals(y.GetBaseDefinition());
        }

        public int GetHashCode(MethodInfo obj)
            => obj.GetBaseDefinition().GetHashCode();
    }
}
