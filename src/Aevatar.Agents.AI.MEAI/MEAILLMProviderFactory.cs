using System.ClientModel;
using System.ClientModel.Primitives;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using System.Net.Http;
using Azure;
using Azure.AI.OpenAI;
using Aevatar.Agents.AI.MEAI.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Aevatar.Agents.AI.MEAI;

// ReSharper disable InconsistentNaming
/// <summary>
/// LLM provider factory implemented using Microsoft.Extensions.AI
/// </summary>
public sealed class MEAILLMProviderFactory : LLMProviderFactoryBase
{
    private readonly IServiceProvider _serviceProvider;

    public MEAILLMProviderFactory(IOptions<LLMProvidersConfig> configuration, ILogger<MEAILLMProviderFactory> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger)
    {
        _serviceProvider = serviceProvider;
        
        // Log all configured providers
        var providerNames = string.Join(", ", Config.Providers.Keys);
        Logger.LogInformation("[MEAIFactory] Configured providers: [{Providers}], Default: {Default}", 
            providerNames, Config.Default);
        
        RegisterProviders();
    }

    public override IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation(
            "[MEAIFactory] Creating provider: Name={Name}, ProviderType={Type}, Model={Model}, Endpoint={Endpoint}",
            providerConfig.Name, providerConfig.ProviderType, providerConfig.Model, providerConfig.Endpoint ?? "(default)");
        
        try
        {
            var chatClient = CreateChatClient(providerConfig);
            var logger = _serviceProvider.GetRequiredService<ILogger<MEAILLMProvider>>();
            Logger.LogInformation("[MEAIFactory] Provider '{Name}' created successfully", providerConfig.Name);
            return new MEAILLMProvider(chatClient, providerConfig, logger, BuildPolicy(providerConfig));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[MEAIFactory] Failed to create provider '{Name}': {Message}", 
                providerConfig.Name, ex.Message);
            throw;
        }
    }

    private static LLMCallPolicy BuildPolicy(LLMProviderConfig config)
    {
        // ------------------------------------------------------------
        //  Keep CallTimeout aligned with client NetworkTimeout.
        //  Default is already 10 minutes, but allow per-provider override.
        // ------------------------------------------------------------
        var timeoutMs = config.TimeoutMilliseconds > 0
            ? config.TimeoutMilliseconds
            : (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        return LLMCallPolicy.Default with { CallTimeout = timeout };
    }

    private IChatClient CreateChatClient(LLMProviderConfig config)
    {
        // Codex OAuth: use dedicated Responses API client
        if (IsCodexOAuthProvider(config))
            return CreateCodexChatClient(config);

        return config.ProviderType.ToLowerInvariant() switch
        {
            "azureopenai" or "azure_openai" => CreateAzureOpenAIChatClient(config),
            _ => CreateOpenAIChatClient(config)
        };
    }

    private static bool IsCodexOAuthProvider(LLMProviderConfig config)
    {
        return config.ProviderSpecificSettings.TryGetValue("openai-beta", out var beta)
               && string.Equals(beta?.ToString(), "codex-v1", StringComparison.OrdinalIgnoreCase);
    }

    private IChatClient CreateCodexChatClient(LLMProviderConfig config)
    {
        Logger.LogInformation(
            "[MEAIFactory] Creating Codex Responses API client: Model={Model}, Endpoint={Endpoint}",
            config.Model, config.Endpoint ?? "(default)");

        var logger = _serviceProvider.GetRequiredService<ILogger<Internal.CodexResponsesChatClient>>();
        return new Internal.CodexResponsesChatClient(config, logger);
    }

    private IChatClient CreateOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            // NOTE:
            // - We use OpenAI-compatible clients for any non-Azure providerType (e.g. OpenAI, DeepSeek, DashScope, etc.).
            // - So this error is about "missing API key for the selected provider", not necessarily OpenAI service.
            var name = string.IsNullOrWhiteSpace(config.Name) ? "(unknown)" : config.Name;
            var type = string.IsNullOrWhiteSpace(config.ProviderType) ? "(unknown)" : config.ProviderType;
            throw new InvalidOperationException($"API key is required for provider '{name}' (ProviderType={type}).");
        }

        var timeoutMs = config.TimeoutMilliseconds > 0
            ? config.TimeoutMilliseconds
            : (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
        
        var enableDeepSeekThinkingModeFix =
            !string.IsNullOrWhiteSpace(config.Model) &&
            config.Model.Contains("deepseek-reasoner", StringComparison.OrdinalIgnoreCase);
        
        if (enableDeepSeekThinkingModeFix)
        {
            Logger.LogInformation(
                "[MEAIFactory] Enabled DeepSeek thinking-mode fix: inject reasoning_content for assistant messages (Model={Model})",
                config.Model);
        }

        var clientOptions = new OpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider),
            // Allow per-provider override. Default is 10 minutes.
            NetworkTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            // Avoid HttpClient default timeout (100s) fighting our configured timeouts.
            Transport = new HttpClientPipelineTransport(BuildHttpClient(enableDeepSeekThinkingModeFix))
        };

        if (!string.IsNullOrWhiteSpace(config.Endpoint))
            clientOptions.Endpoint = new Uri(config.Endpoint);

        return new ChatClient(config.Model, new ApiKeyCredential(config.ApiKey), clientOptions).AsIChatClient();
    }

    private static HttpClient BuildHttpClient(bool enableDeepSeekThinkingModeFix)
    {
        // ------------------------------------------------------------
        // DeepSeek thinking-mode compatibility:
        // - deepseek-reasoner requires `reasoning_content` for assistant messages.
        // - OpenAI SDK adapter doesn't emit it, so we patch at HTTP layer.
        // ------------------------------------------------------------
        var inner = new HttpClientHandler();
        HttpMessageHandler handler = inner;

        if (enableDeepSeekThinkingModeFix)
        {
            handler = new DeepSeekThinkingModeFixHandler(handler);
        }

        return new HttpClient(handler)
        {
            // Avoid HttpClient default timeout (100s) fighting our configured timeouts.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
    }

    private IChatClient CreateAzureOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI API key is required");

        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint is required");

        var timeoutMs = config.TimeoutMilliseconds > 0
            ? config.TimeoutMilliseconds
            : (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

        var clientOptions = new AzureOpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider),
            // Allow per-provider override. Default is 10 minutes.
            NetworkTimeout = TimeSpan.FromMilliseconds(timeoutMs),
            // Avoid HttpClient default timeout (100s) fighting our configured timeouts.
            Transport = new HttpClientPipelineTransport(new HttpClient
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            })
        };

        var azureClient = new AzureOpenAIClient(
            new Uri(config.Endpoint),
            new AzureKeyCredential(config.ApiKey),
            clientOptions);

        return azureClient.GetChatClient(config.DeploymentName ?? config.Model).AsIChatClient();
    }
}