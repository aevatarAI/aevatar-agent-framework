using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Tool.Evolution;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Injects Hook/Harness configuration into AIGAgentBase instances (best-effort).
///
/// 中文 + ASCII:
/// - 这是一个兼容层：历史上通过反射写属性；现在优先使用 AIGAgentBase 的 type-safe 注入方法。
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
            // Type-safe injection (no reflection).
            if (agent is not AIGAgentBase aiAgent)
                return;

            var options = serviceProvider.GetService<IOptions<AevatarAgentHookOptions>>()?.Value
                          ?? serviceProvider.GetService<AevatarAgentHookOptions>();
            aiAgent.InjectHookOptions(options);

            var evolution = serviceProvider.GetService<IOptions<ToolEvolutionOptions>>()?.Value
                            ?? serviceProvider.GetService<ToolEvolutionOptions>();
            aiAgent.InjectToolEvolutionOptions(evolution);

            var hooks = serviceProvider.GetServices<IAevatarAgentHook>();
            aiAgent.InjectAdditionalHooks(hooks);
        }
        catch
        {
            // Best-effort: never fail agent creation due to hook injection.
        }
    }
}


