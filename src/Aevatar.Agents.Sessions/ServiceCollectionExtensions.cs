using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Sessions.Runtime;
using Aevatar.Agents.Tooling;
using Aevatar.Agents.Tooling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarCognitiveSessions(
        this IServiceCollection services,
        Action<CognitiveSessionOptions>? configure = null)
    {
        if (configure != null)
        {
            services.AddOptions<CognitiveSessionOptions>().Configure(configure);
        }
        else
        {
            services.TryAddSingleton<IOptions<CognitiveSessionOptions>>(
                _ => Options.Create(new CognitiveSessionOptions()));
        }

        services.TryAddSingleton<GlobalAgentYamlRegistry>();
        services.TryAddSingleton<RoleAgentFactory>();
        services.TryAddSingleton<ISessionStore, SessionStore>();
        services.TryAddSingleton<CognitiveSessionService>();
        return services;
    }

    public static IServiceCollection AddAevatarSessionRuntime(
        this IServiceCollection services,
        Action<SessionRuntimeOptions>? configure = null)
    {
        services.AddAevatarCognitiveSessions();

        if (configure != null)
        {
            services.AddOptions<SessionRuntimeOptions>().Configure(configure);
        }
        else
        {
            services.TryAddSingleton<IOptions<SessionRuntimeOptions>>(
                _ => Options.Create(new SessionRuntimeOptions()));
        }

        services.TryAddSingleton<IAgentMessageStreamResolver, AgentMessageStreamResolver>();
        services.TryAddSingleton<AgentBootstrapper>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISessionAgUiBootstrapper, DefaultSessionAgUiBootstrapper>());
        services.TryAddSingleton<IWorkflowCatalog, SessionWorkflowCatalog>();
        services.TryAddSingleton<WorkflowMeshCompiler>();
        services.TryAddSingleton<WorkflowMeshService>();
        services.TryAddSingleton<SessionWorkflowRunner>();
        services.TryAddSingleton<SessionRuntime>();
        return services;
    }

    public static IServiceCollection AddAevatarSessionTooling(
        this IServiceCollection services,
        Action<AgentToolingOptions>? configure = null)
    {
        services.AddAevatarAgentTooling(configure);
        return services;
    }
}
