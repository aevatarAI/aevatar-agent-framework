using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Agent LLM Provider Factory automatic injector
/// Injects ILLMProviderFactory into Agent instances after creation
/// </summary>
public static class AIAgentLLMProviderFactoryInjector
{
    /// <summary>
    /// Inject ILLMProviderFactory into Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectLLMProviderFactory(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();
        var baseType = agentType.BaseType;

        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.Name == "AIGAgentBase")
            {
                // ============================================================
                //  Framework-level fix:
                //  - MS.DI supports multiple registrations for the same service type.
                //  - GetService() returns ONLY the last one (previous behavior).
                //  - We need ALL factories so the agent can use multiple providers
                //    (e.g. MEAI + LLMTornado in the same app).
                // ============================================================
                var factories = serviceProvider.GetServices<ILLMProviderFactory>().ToList();
                if (factories.Count > 0)
                {
                    object factory;
                    if (factories.Count == 1)
                    {
                        factory = factories[0];
                    }
                    else
                    {
                        // Best-effort logger: avoid requiring ILogger in apps that don't use it.
                        ILogger? logger = null;
                        try
                        {
                            logger = serviceProvider.GetService<ILogger<CompositeLLMProviderFactory>>();
                        }
                        catch
                        {
                            // best-effort only
                        }

                        factory = new CompositeLLMProviderFactory(factories, logger);
                    }

                    var factoryProperty = FindFactoryProperty(agentType);
                    if (factoryProperty != null && factoryProperty.CanWrite)
                    {
                        try
                        {
                            factoryProperty.SetValue(agent, factory);
                        }
                        catch (Exception)
                        {
                            // Silently handle injection failure
                        }
                    }
                }

                break;
            }

            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Find ILLMProviderFactory property in agent type hierarchy
    /// </summary>
    private static PropertyInfo? FindFactoryProperty(Type agentType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Check current type
        var properties = agentType.GetProperties(bindingFlags);
        foreach (var property in properties)
        {
            if (property.Name == "LLMProviderFactory" &&
                typeof(ILLMProviderFactory).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }
        }

        // Check base type recursively
        if (agentType.BaseType != null && agentType.BaseType != typeof(object))
        {
            return FindFactoryProperty(agentType.BaseType);
        }

        return null;
    }

    /// <summary>
    /// Check if agent type has ILLMProviderFactory property
    /// </summary>
    public static bool HasLLMProviderFactory(object target)
    {
        if (target == null)
            return false;

        var targetType = target.GetType();
        var property = FindFactoryProperty(targetType);
        return property != null;
    }
}
