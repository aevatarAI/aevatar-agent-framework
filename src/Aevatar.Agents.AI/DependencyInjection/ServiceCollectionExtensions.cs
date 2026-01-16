using Aevatar.Agents.AI.LLMTornado;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.DependencyInjection;

// ============================================================
//  AddAevatarLLMProviders
//
//  中文说明：
//  - 聚合注册：默认同时启用 MEAI + LLMTornado
//  - 无反射：通过显式项目引用保证可用性与可重构性
//
//  使用方式：
//    services.AddAevatarLLMProviders();
//
//  依赖：
//  - Aevatar.Agents.AI.MEAI
//  - Aevatar.Agents.AI.LLMTornado
//
//  NOTE:
//  - 框架层 Injector 会把多个 ILLMProviderFactory 自动组合注入给 AIGAgentBase。
// ============================================================
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarLLMProviders(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        services.AddMEAI();
        services.AddAevatarLLMTornado();

        return services;
    }
}


