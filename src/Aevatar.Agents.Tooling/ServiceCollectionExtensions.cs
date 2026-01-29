using Aevatar.Agents.Tooling.Catalog;
using Aevatar.Agents.Tooling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Tooling;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarAgentTooling(
        this IServiceCollection services,
        Action<AgentToolingOptions>? configure = null)
    {
        if (configure != null)
        {
            services.AddOptions<AgentToolingOptions>().Configure(configure);
        }
        else
        {
            services.TryAddSingleton<IOptions<AgentToolingOptions>>(
                _ => Microsoft.Extensions.Options.Options.Create(new AgentToolingOptions()));
        }

        services.TryAddSingleton<AgentToolCatalog>();
        return services;
    }
}
