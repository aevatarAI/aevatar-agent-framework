using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Injects Hook/Harness configuration into AIGAgentBase instances (best-effort).
///
/// 中文 + ASCII:
/// - 通过反射注入（与 ToolManager/MemoryStore 注入一致）。
/// - 不注入则走默认值：内置 hooks + 默认 options（安全保守）。
/// </summary>
public static class AIAgentHookInjector
{
    public static void InjectHooks(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        try
        {
            InjectHookOptions(agent, serviceProvider);
            InjectAdditionalHooks(agent, serviceProvider);
        }
        catch
        {
            // Best-effort: never fail agent creation due to hook injection.
        }
    }

    private static void InjectHookOptions(IGAgent agent, IServiceProvider serviceProvider)
    {
        var property = FindProperty(agent.GetType(), "HookOptions", typeof(AevatarAgentHookOptions));
        if (property == null || !property.CanWrite)
            return;

        // Prefer IOptions<T> so host can bind from configuration.
        var optionsWrapper = serviceProvider.GetService(typeof(IOptions<AevatarAgentHookOptions>)) as IOptions<AevatarAgentHookOptions>;
        if (optionsWrapper?.Value != null)
        {
            property.SetValue(agent, optionsWrapper.Value);
            return;
        }

        var options = serviceProvider.GetService(typeof(AevatarAgentHookOptions)) as AevatarAgentHookOptions;
        if (options != null)
        {
            property.SetValue(agent, options);
        }
    }

    private static void InjectAdditionalHooks(IGAgent agent, IServiceProvider serviceProvider)
    {
        var property = FindProperty(agent.GetType(), "AdditionalHooks", typeof(IEnumerable<IAevatarAgentHook>));
        if (property == null || !property.CanWrite)
            return;

        // Default DI container supports resolving IEnumerable<T> (empty when none registered).
        var hooks = serviceProvider.GetService(typeof(IEnumerable<IAevatarAgentHook>));
        if (hooks != null)
        {
            property.SetValue(agent, hooks);
        }
    }

    private static PropertyInfo? FindProperty(Type agentType, string name, Type expectedType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && expectedType.IsAssignableFrom(property.PropertyType))
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}


