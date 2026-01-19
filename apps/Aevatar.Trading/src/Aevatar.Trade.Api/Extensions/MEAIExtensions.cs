using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.MEAI.DependencyInjection;

namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// Microsoft.Extensions.AI LLM Provider extensions
/// </summary>
public static class MEAIExtensions
{
    /// <summary>
    /// Add MEAI LLM Provider
    /// </summary>
    public static IServiceCollection AddMEAILLMProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============================================================
        //  LLM Providers Configuration
        //
        //  Required: LLMProviders (loaded from ~/.aevatar/secrets.json or
        //  appsettings.secrets.json). Legacy "LLM" is no longer supported.
        // ============================================================

        var llmProvidersSection = configuration.GetSection("LLMProviders");
        if (!llmProvidersSection.Exists())
        {
            throw new InvalidOperationException(
                "LLMProviders config is required. Please configure ~/.aevatar/secrets.json " +
                "or apps/Aevatar.Trading/src/Aevatar.Trade.Api/appsettings.secrets.json.");
        }

        services.Configure<LLMProvidersConfig>(llmProvidersSection);

        // Register MEAI provider factory + embedding factory.
        services.AddMEAI();

        return services;
    }
}
