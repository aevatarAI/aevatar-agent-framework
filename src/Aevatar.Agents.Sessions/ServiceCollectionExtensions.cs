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

        services.TryAddSingleton<ISessionStore, SessionStore>();
        services.TryAddSingleton<CognitiveSessionService>();
        return services;
    }
}
