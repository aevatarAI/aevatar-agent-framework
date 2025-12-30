using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core;
using Aevatar.Agents.Persistence.Graph.Core.IR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Persistence.InMemory.Graph;

/// <summary>
/// InMemory Graph Provider 的 DI 扩展。
/// </summary>
public static class GraphInMemoryServiceCollectionExtensions
{
    /// <summary>
    /// 注册 InMemory Graph provider（开发/测试最快，无外部依赖）。
    /// </summary>
    public static IServiceCollection AddAevatarGraphInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryGraphStore>();
        services.TryAddSingleton<IGraphCompiler<GraphOperation>, PassThroughGraphCompiler>();
        services.TryAddSingleton<IGraphExecutor<GraphOperation>, InMemoryGraphExecutor>();
        services.TryAddSingleton<IGraphClient, GraphClient<GraphOperation>>();

        return services;
    }

    /// <summary>
    /// 兼容命名：Graph.InMemory 的旧用法/口径（仍然注册 InMemory provider）。
    /// </summary>
    public static IServiceCollection AddAevatarGraphMemory(this IServiceCollection services)
        => services.AddAevatarGraphInMemory();
}


