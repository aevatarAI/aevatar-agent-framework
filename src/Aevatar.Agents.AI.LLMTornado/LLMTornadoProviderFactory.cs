using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;
using LlmTornado;
using LlmTornado.Code;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.LLMTornado;

/// <summary>
/// LlmTornado implementation of LLM Provider Factory.
/// Supports OpenAI, Anthropic, Google (Gemini), Azure, Groq, Cohere.
/// Also supports OpenAI-compatible APIs (DeepSeek, Moonshot, etc.) via custom endpoint.
/// Local deployments: Ollama, vLLM, LocalAI.
/// </summary>
public sealed class LLMTornadoProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public LLMTornadoProviderFactory(IOptions<LLMProvidersConfig> configuration,
        ILogger<LLMTornadoProviderFactory> logger, IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        RegisterProviders();
    }

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        var providerTypeStr = providerConfig.ProviderType?.ToLowerInvariant();

        // ------------------------------------------------------------
        // Claude Agent SDK (NOT Claude model API)
        // - Implemented as a dedicated IAevatarLLMProvider that executes an external runner process.
        // - We must branch BEFORE LlmTornado routing and ApiKey checks.
        // ------------------------------------------------------------
        if (providerTypeStr is "claude_agent_sdk" or "claude-agent-sdk")
        {
            var claudeLogger = _serviceProvider.GetRequiredService<ILogger<ClaudeAgentSdkProvider>>();
            return new ClaudeAgentSdkProvider(providerConfig, claudeLogger);
        }

        var providerType = ParseProvider(providerTypeStr);
        var endpoint = providerConfig.Endpoint;
        var model = providerConfig.Model;

        // Local deployments (Ollama, vLLM, LocalAI) don't require ApiKey
        var isLocalProvider = providerTypeStr is "ollama" or "vllm" or "localai";
        var apiKey = providerConfig.ApiKey ??
                     (isLocalProvider ? "ollama" : throw new ArgumentException("ApiKey is required"));

        // Create TornadoApi with correct endpoint handling
        TornadoApi api;

        if (providerTypeStr == "ollama")
        {
            // Ollama: OpenAI-compatible API, default port 11434/v1
            // Ollama's OpenAI-compatible endpoint is /v1
            var ollamaEndpoint = !string.IsNullOrEmpty(endpoint)
                ? endpoint
                : AevatarLLMTornadoConstants.OllamaDefaultEndpoint;
            api = new TornadoApi(new Uri(ollamaEndpoint), apiKey, LLmProviders.OpenAi);
        }
        else if (!string.IsNullOrEmpty(endpoint))
        {
            // Custom endpoint (DeepSeek, Moonshot, vLLM, LocalAI, etc.)
            // Use the constructor designed for self-hosted / custom providers
            api = new TornadoApi(new Uri(endpoint), apiKey, providerType);
        }
        else
        {
            // Native provider (OpenAI, Anthropic, Google, etc.)
            // Use ProviderAuthentication for built-in provider routing
            var auth = new ProviderAuthentication(providerType, apiKey);
            api = new TornadoApi([auth]);
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<LLMTornadoProvider>>();
        return new LLMTornadoProvider(api, logger, providerType, model);
    }

    private static LLmProviders ParseProvider(string? providerType)
    {
        return providerType switch
        {
            "openai" => LLmProviders.OpenAi,
            "anthropic" or "claude" => LLmProviders.Anthropic,
            "azure" or "azureopenai" => LLmProviders.AzureOpenAi,
            "google" or "gemini" => LLmProviders.Google,
            "cohere" => LLmProviders.Cohere,
            "groq" => LLmProviders.Groq,
            "ollama" or "vllm" => LLmProviders.Custom,
            // OpenAI-compatible APIs
            "deepseek" or "moonshot" or "qwen" or "openai_compatible" => LLmProviders.OpenAi,
            _ => LLmProviders.OpenAi
        };
    }
}
