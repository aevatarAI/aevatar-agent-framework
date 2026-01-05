using System.Collections.Generic;

namespace Aevatar.Agents.AI.Abstractions.Configuration;

/// <summary>
/// LLM provider configuration
/// <para/>
/// Configure multiple LLM providers in appsettings.json
/// </summary>
public class LLMProviderConfig
{
    /// <summary>
    /// Provider name (unique identifier, e.g., openai-gpt4, azure-gpt35, local-llama, etc.)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Provider type (OpenAI, AzureOpenAI, Local, etc)
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// API key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model name (e.g., gpt-4, gpt-3.5-turbo, llama2-70b, etc.)
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// API endpoint (optional, for Azure or local models)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Deployment name (Azure OpenAI specific)
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Temperature parameter (0-2)
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens
    /// </summary>
    public int MaxTokens { get; set; } = AevatarAIDefaults.DefaultMaxOutputTokens;

    /// <summary>
    /// Timeout duration (milliseconds)
    /// </summary>
    // NOTE:
    // - Default to 10 minutes to match current MEAI/OpenAI client behavior (large context + tool loops can be slow).
    // - Apps can override per-provider in configuration.
    public int TimeoutMilliseconds { get; set; } = 600_000;

    /// <summary>
    /// Whether to enable streaming response
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    /// Provider-specific settings
    /// </summary>
    public Dictionary<string, object> ProviderSpecificSettings { get; set; } = new();

    /// <summary>
    /// Embedding channel configuration (optional)
    /// </summary>
    public LLMEmbeddingConfig? Embeddings { get; set; }
}

/// <summary>
/// LLM providers collection configuration
/// <para/>
/// Configuration example in appsettings.json:
/// <code>
/// {
///   "LLMProviders": {
///     "default": "openai-gpt4",
///     "providers": {
///       "openai-gpt4": {
///         "providerType": "OpenAI",
///         "apiKey": "${OPENAI_API_KEY}",
///         "model": "gpt-4",
///         "temperature": 0.7
///       },
///       "azure-gpt35": {
///         "providerType": "AzureOpenAI",
///         "apiKey": "${AZURE_API_KEY}",
///         "endpoint": "https://your-resource.openai.azure.com",
///         "deploymentName": "gpt-35-turbo",
///         "model": "gpt-3.5-turbo"
///       },
///       "local-llama": {
///         "providerType": "Ollama",
///         "endpoint": "http://localhost:11434",
///         "model": "llama2:70b"
///       }
///     }
///   }
/// }
/// </code>
/// </summary>
public class LLMProvidersConfig
{
    /// <summary>
    /// Default provider name
    /// </summary>
    public string Default { get; set; }

    /// <summary>
    /// Provider dictionary (key: provider name, value: configuration)
    /// </summary>
    public Dictionary<string, LLMProviderConfig> Providers { get; set; } = new();
}

/// <summary>
/// Embedding configuration, used to drive IEmbeddingGenerator
/// </summary>
public class LLMEmbeddingConfig
{
    /// <summary>
    /// Whether to enable Embedding channel (default true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedding provider type (OpenAI, AzureOpenAI, Ollama, etc.).
    /// If not specified, defaults to main configuration's ProviderType.
    /// </summary>
    public string? ProviderType { get; set; }

    /// <summary>
    /// Embedding model name (OpenAI scenario)
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Embedding deployment name (Azure OpenAI scenario)
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Overridable Endpoint
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Overridable API Key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Dimensions (some models require manual specification)
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Provider-specific extension fields
    /// </summary>
    public Dictionary<string, object> ProviderSpecificSettings { get; set; } = new();
}