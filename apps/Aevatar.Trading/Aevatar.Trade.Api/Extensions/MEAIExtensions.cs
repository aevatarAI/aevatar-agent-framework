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
        //  Preferred: LLMProviders section (shared across demos/services).
        //  Compatible fallback: legacy LLM section in this trade API.
        // ============================================================

        var llmProvidersSection = configuration.GetSection("LLMProviders");
        if (llmProvidersSection.Exists())
        {
            services.Configure<LLMProvidersConfig>(llmProvidersSection);
        }
        else
        {
            // Fallback mapping from "LLM" (simple single-provider config)
            services.Configure<LLMProvidersConfig>(cfg =>
            {
                var llm = configuration.GetSection("LLM");
                var providerType = llm["Provider"] ?? "OpenAI";
                var model = llm["Model"] ?? "gpt-4";
                var apiKey = llm["ApiKey"]
                             ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                             ?? string.Empty;

                var temperature = double.TryParse(llm["Temperature"], out var t) ? t : 0.3;
                var maxTokens = int.TryParse(llm["MaxTokens"], out var m) ? m : 2000;

                const string providerName = "trade-default";

                cfg.Default = providerName;
                cfg.Providers[providerName] = new LLMProviderConfig
                {
                    Name = providerName,
                    ProviderType = providerType,
                    ApiKey = apiKey,
                    Model = model,
                    Temperature = temperature,
                    MaxTokens = maxTokens,
                    // Leave endpoint/deployment empty by default; user can switch to LLMProviders for Azure/OpenAI overrides
                };
            });
        }

        // Register MEAI provider factory + embedding factory.
        services.AddMEAI();

        return services;
    }
}
