using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.LLMTornado;
using Aevatar.Agents.AI.MEAI.DependencyInjection;

namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// LLM Provider registration (MEAI / LLMTornado)
/// </summary>
public static class MEAIExtensions
{
    /// <summary>
    /// Add LLM Provider (auto-select between MEAI and LLMTornado).
    ///
    /// Why:
    /// - MEAI provider factory is OpenAI-compatible only (plus AzureOpenAI).
    /// - LLMTornado supports native providers like Google(Gemini)/Anthropic/...
    /// - User secrets UI (Aevatar.Secrets.Api) can configure Google providerType, so Trade API must support it.
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

            // Select provider factory.
            // - Allow explicit override via config:
            //   - LLMProviders:Factory = "MEAI" | "LLMTornado"
            // - Otherwise auto:
            //   - Google/Gemini/Anthropic -> LLMTornado
            //   - Default -> MEAI
            var factoryOverride = llmProvidersSection["Factory"];
            var useTornado =
                string.Equals(factoryOverride, "LLMTornado", StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(factoryOverride) && ShouldUseLLMTornado(llmProvidersSection.Get<LLMProvidersConfig>()));

            if (useTornado)
            {
                services.AddAevatarLLMTornado();
            }
            else
            {
                services.AddMEAI();
            }
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

            // Legacy "LLM" fallback is OpenAI-compatible; MEAI is sufficient here.
            services.AddMEAI();
        }

        return services;
    }

    private static bool ShouldUseLLMTornado(LLMProvidersConfig? cfg)
    {
        if (cfg?.Providers == null || cfg.Providers.Count == 0)
            return false;

        foreach (var p in cfg.Providers.Values)
        {
            var type = (p?.ProviderType ?? "").Trim();
            var model = (p?.Model ?? "").Trim();

            // Native provider types (not OpenAI-compatible):
            if (type.Equals("google", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("gemini", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("claude", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("cohere", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Heuristic: Gemini model naming.
            if (model.Contains("gemini", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
