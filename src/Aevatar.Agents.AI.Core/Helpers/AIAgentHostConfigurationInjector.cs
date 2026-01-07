using System.Reflection;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Best-effort injection of host <see cref="IConfiguration"/> into AI agents.
/// <para/>
/// 中文 + ASCII:
/// - We keep this as explicit injection (similar to ToolManager/EmbeddingFactory injectors).
/// - Agents should not have to require IConfiguration in their constructors just to enable MCP auto-connect.
/// </summary>
public static class AIAgentHostConfigurationInjector
{
    public static void InjectHostConfiguration(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindHostConfigurationProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var cfg = serviceProvider.GetService(typeof(IConfiguration));
        if (cfg == null)
            return;

        try
        {
            property.SetValue(agent, cfg);
        }
        catch
        {
            // Best-effort: never fail agent creation due to configuration injection.
        }
    }

    private static PropertyInfo? FindHostConfigurationProperty(Type agentType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                "HostConfiguration",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && typeof(IConfiguration).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}


