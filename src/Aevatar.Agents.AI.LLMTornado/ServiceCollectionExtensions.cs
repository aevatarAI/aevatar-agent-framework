using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using LlmTornado;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.LLMTornado;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarLLMTornado(this IServiceCollection services)
    {
        // Register the factory
        services.AddSingleton<ILLMProviderFactory, LLMTornadoProviderFactory>();
        return services;
    }

    public static IServiceCollection AddAevatarLLMTornado(this IServiceCollection services,
        Action<LlmTornadoConfig> configure)
    {
        var config = new LlmTornadoConfig();
        configure(config);

        services.AddSingleton(config);

        // Register the factory
        services.AddSingleton<ILLMProviderFactory, LLMTornadoProviderFactory>();

        // Also register the provider directly for simple use cases
        services.AddSingleton<TornadoApi>(sp => new TornadoApi(config.ApiKey, config.Provider));

        // =========================================================================
        //  LLMTornadoProvider requires explicit construction
        //  Constructor: (TornadoApi, ILogger, LLmProviders, modelName, policy?)
        // =========================================================================
        services.AddSingleton<IAevatarLLMProvider>(sp =>
        {
            var api = sp.GetRequiredService<TornadoApi>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LLMTornadoProvider>>();
            return new LLMTornadoProvider(api, logger, config.Provider, config.Model, instanceName: "default");
        });

        return services;
    }
}
