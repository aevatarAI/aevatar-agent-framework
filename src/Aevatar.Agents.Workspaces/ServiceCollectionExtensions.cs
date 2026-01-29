using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Workspaces.Core;
using Aevatar.Agents.Workspaces.Events;
using Aevatar.Agents.Workspaces.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Workspaces;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarRoleWorkspace(
        this IServiceCollection services,
        Action<RoleWorkspaceOptions>? configure = null)
    {
        if (configure != null)
        {
            services.AddOptions<RoleWorkspaceOptions>().Configure(configure);
        }
        else
        {
            services.TryAddSingleton<IOptions<RoleWorkspaceOptions>>(
                _ => Options.Create(new RoleWorkspaceOptions()));
        }

        services.TryAddSingleton<RoleAgUiHub>();
        services.TryAddSingleton<RoleWorkspaceService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventModuleFactory, RoleWorkspaceEventModuleFactory>());
        return services;
    }
}
