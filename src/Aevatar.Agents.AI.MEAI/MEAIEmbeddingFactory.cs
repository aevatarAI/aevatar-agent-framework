using System.ClientModel;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.MEAI.Internal;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;

namespace Aevatar.Agents.AI.MEAI;

public sealed class MEAIEmbeddingFactory : IAIAgentEmbeddingFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MEAIEmbeddingFactory> _logger;

    public MEAIEmbeddingFactory(IServiceProvider serviceProvider, ILogger<MEAIEmbeddingFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        if (providerConfig == null)
            throw new ArgumentNullException(nameof(providerConfig));

        if (providerConfig.Embeddings == null)
            return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null);

        try
        {
            var generator = CreateGenerator(providerConfig, providerConfig.Embeddings);
            return Task.FromResult(generator);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create embedding generator for provider {Provider}", providerConfig.Name);
            return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null);
        }
    }

    private IEmbeddingGenerator<string, Embedding<float>>? CreateGenerator(
        LLMProviderConfig providerConfig,
        LLMEmbeddingConfig embeddingConfig)
    {
        // Prefer ProviderType from embedding config, fall back to main config's ProviderType if empty
        var providerType = (!string.IsNullOrWhiteSpace(embeddingConfig.ProviderType) 
            ? embeddingConfig.ProviderType 
            : providerConfig.ProviderType).ToLowerInvariant();

        return providerType switch
        {
            "azureopenai" or "azure_openai" or "azure" =>
                CreateAzureEmbeddingGenerator(providerConfig, embeddingConfig),
            _ => CreateOpenAIEmbeddingGenerator(providerConfig, embeddingConfig)
        };
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateOpenAIEmbeddingGenerator(
        LLMProviderConfig providerConfig,
        LLMEmbeddingConfig embeddingConfig)
    {
        var apiKey = embeddingConfig.ApiKey ?? providerConfig.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI embeddings require an API key.");

        var model = embeddingConfig.Model ?? providerConfig.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("OpenAI embeddings require a model identifier.");

        var endpoint = embeddingConfig.Endpoint ?? providerConfig.Endpoint;
        var clientOptions = new OpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider)
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            clientOptions.Endpoint = new Uri(endpoint);
        }

        var embeddingClient = new EmbeddingClient(model, new ApiKeyCredential(apiKey), clientOptions);
        return embeddingClient.AsIEmbeddingGenerator(embeddingConfig.Dimensions);
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateAzureEmbeddingGenerator(
        LLMProviderConfig providerConfig,
        LLMEmbeddingConfig embeddingConfig)
    {
        var apiKey = embeddingConfig.ApiKey ?? providerConfig.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Azure OpenAI embeddings require an API key.");

        var endpoint = embeddingConfig.Endpoint ?? providerConfig.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Azure OpenAI embeddings require an endpoint.");

        var deployment = embeddingConfig.DeploymentName
                         ?? providerConfig.DeploymentName
                         ?? embeddingConfig.Model
                         ?? providerConfig.Model;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException("Azure OpenAI embeddings require a deployment name.");

        var clientOptions = new AzureOpenAIClientOptions
        {
            ClientLoggingOptions = MEAIClientLoggingOptionsBuilder.Create(_serviceProvider)
        };

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
        var embeddingClient = azureClient.GetEmbeddingClient(deployment);
        return embeddingClient.AsIEmbeddingGenerator(embeddingConfig.Dimensions);
    }
}
