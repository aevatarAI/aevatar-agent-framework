using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Embeddings;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Automatically inject IAIAgentEmbeddingFactory
/// </summary>
public static class AIAgentEmbeddingFactoryInjector
{
    public static void InjectEmbeddingFactory(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var factory = serviceProvider.GetService(typeof(IAIAgentEmbeddingFactory)) as IAIAgentEmbeddingFactory;
        if (factory == null)
            return;

        var property = FindFactoryProperty(agent.GetType());
        if (property != null && property.CanWrite)
        {
            try
            {
                property.SetValue(agent, factory);
            }
            catch (Exception)
            {
                // Ignore injection errors to avoid breaking agent creation
            }
        }
    }

    public static bool HasEmbeddingFactory(object target)
    {
        if (target == null)
            return false;

        return FindFactoryProperty(target.GetType()) != null;
    }

    private static PropertyInfo? FindFactoryProperty(Type type)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        var properties = type.GetProperties(bindingFlags);
        foreach (var property in properties)
        {
            if (property.Name == "EmbeddingFactory" &&
                typeof(IAIAgentEmbeddingFactory).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }
        }

        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            return FindFactoryProperty(type.BaseType);
        }

        return null;
    }
}

