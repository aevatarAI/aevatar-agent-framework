using System.ClientModel;
using System.ClientModel.Primitives;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
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
            return new MEAILLMProvider(chatClient, providerConfig, logger);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[MEAIFactory] Failed to create provider '{Name}': {Message}", 
                providerConfig.Name, ex.Message);
            throw;
        }
    }

    private IChatClient CreateChatClient(LLMProviderConfig config)
    {
        return config.ProviderType.ToLowerInvariant() switch
        {
            "azureopenai" or "azure_openai" => CreateAzureOpenAIChatClient(config),
            _ => CreateOpenAIChatClient(config)
        };
    }

    private IChatClient CreateOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is required");

        var clientOptions = new OpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider),
            // Increase network timeout for large token generation (20K+ tokens can take 5+ minutes)
            NetworkTimeout = TimeSpan.FromMinutes(10)
        };

        if (!string.IsNullOrWhiteSpace(config.Endpoint))
            clientOptions.Endpoint = new Uri(config.Endpoint);

        return new ChatClient(config.Model, new ApiKeyCredential(config.ApiKey), clientOptions).AsIChatClient();
    }

    private IChatClient CreateAzureOpenAIChatClient(LLMProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI API key is required");

        if (string.IsNullOrEmpty(config.Endpoint))
            throw new InvalidOperationException("Azure OpenAI endpoint is required");

        var clientOptions = new AzureOpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider),
            // Increase network timeout for large token generation (20K+ tokens can take 5+ minutes)
            NetworkTimeout = TimeSpan.FromMinutes(10)
        };

        var azureClient = new AzureOpenAIClient(
            new Uri(config.Endpoint),
            new AzureKeyCredential(config.ApiKey),
            clientOptions);

        return azureClient.GetChatClient(config.DeploymentName ?? config.Model).AsIChatClient();
    }
}