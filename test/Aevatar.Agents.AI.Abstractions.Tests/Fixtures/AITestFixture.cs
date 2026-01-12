using System.IO;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Abstractions.Tests.Embedding;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Abstractions.Tests.ToolManager;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Core.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Abstractions.Tests.Fixtures;

/// <summary>
/// Test fixture for AI abstraction tests with DI and configuration support
/// Inherits from CoreTestFixture to get basic agent infrastructure (EventPublisher, StateStore, ConfigStore)
/// </summary>
public class AITestFixture : CoreTestFixture
{
    private IConfiguration? _configuration;
    
    public IConfiguration Configuration => _configuration ?? throw new InvalidOperationException("Configuration not initialized");
    public ILogger<AITestFixture> Logger => ServiceProvider.GetRequiredService<ILogger<AITestFixture>>();
    public ILLMProviderFactory LLMProviderFactory => ServiceProvider.GetRequiredService<ILLMProviderFactory>();
    public MockLLMProvider MockLLMProvider => (MockLLMProvider)LLMProviderFactory.GetProvider("test-provider");
    public IAevatarToolManager ToolManager => ServiceProvider.GetRequiredService<IAevatarToolManager>();
    public IGAgentFactory GAgentFactory => ServiceProvider.GetRequiredService<IGAgentFactory>();

    /// <summary>
    /// Override to add AI-specific services on top of core services
    /// This is called from base constructor, so we initialize configuration here
    /// </summary>
    protected override void ConfigureAdditionalServices(IServiceCollection services)
    {
        base.ConfigureAdditionalServices(services);
        
        // Initialize configuration first
        var assemblyLocation = Path.GetDirectoryName(typeof(AITestFixture).Assembly.Location);
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(assemblyLocation ?? Directory.GetCurrentDirectory())
            .AddInMemoryCollection(GetDefaultConfiguration()) // Default values first
            .AddJsonFile("appsettings.test.json", optional: true, reloadOnChange: false); // JSON overrides defaults

        _configuration = configBuilder.Build();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                .SetMinimumLevel(LogLevel.Debug);
        });

        // Add configuration
        services.AddSingleton(_configuration);

        // Configure LLM providers
        services.Configure<LLMProvidersConfig>(_configuration.GetSection("LLMProviders"));

        // Register test implementations
        services.AddSingleton<ILLMProviderFactory, MockLLMProviderFactory>();
        services.AddSingleton<IAIAgentEmbeddingFactory, MockEmbeddingFactory>();

        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();

        // Add Options
        services.AddOptions();

        // Register tool manager with a default tool
        services.AddSingleton<IAevatarToolManager>(sp =>
        {
            var toolManager = new MockToolManager();
            // Register a default tool for testing
            toolManager.RegisterTool(new ToolDefinition
            {
                Name = "calculator",
                Description = "Perform calculations",
                Category = ToolCategory.Utility,
                IsEnabled = true,
                Parameters = new ToolParameters
                {
                    Items = new Dictionary<string, ToolParameter>
                    {
                        ["expression"] = new() { Type = "string", Description = "Math expression", Required = true }
                    },
                    Required = new List<string> { "expression" }
                }
            });
            return toolManager;
        });

        // Add any other test services
        ConfigureTestServices(services);
    }

    /// <summary>
    /// Override this method in derived fixtures to add specific test services
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Can be overridden in derived classes
    }

    private Dictionary<string, string?> GetDefaultConfiguration()
    {
        return new Dictionary<string, string?>
        {
            // Default LLM configuration
            ["LLMProviders:Default"] = "test-provider",
            ["LLMProviders:Providers:test-provider:Name"] = "test-provider",
            ["LLMProviders:Providers:test-provider:ProviderType"] = "test",
            ["LLMProviders:Providers:test-provider:Model"] = "test-model",
            ["LLMProviders:Providers:test-provider:ApiKey"] = "test-api-key",
            ["LLMProviders:Providers:test-provider:Temperature"] = "0.7",
            ["LLMProviders:Providers:test-provider:MaxTokens"] = "2048",

            ["LLMProviders:Providers:openai-gpt4:Name"] = "openai-gpt4",
            ["LLMProviders:Providers:openai-gpt4:ProviderType"] = "openai",
            ["LLMProviders:Providers:openai-gpt4:Model"] = "gpt-4",
            ["LLMProviders:Providers:openai-gpt4:ApiKey"] = "fake-openai-key",
            ["LLMProviders:Providers:openai-gpt4:Temperature"] = "0.5",
            ["LLMProviders:Providers:openai-gpt4:MaxTokens"] = "4096",

            ["LLMProviders:Providers:azure-gpt35:Name"] = "azure-gpt35",
            ["LLMProviders:Providers:azure-gpt35:ProviderType"] = "azure",
            ["LLMProviders:Providers:azure-gpt35:Model"] = "gpt-35-turbo",
            ["LLMProviders:Providers:azure-gpt35:DeploymentName"] = "gpt35-deployment",
            ["LLMProviders:Providers:azure-gpt35:ApiKey"] = "fake-azure-key",
            ["LLMProviders:Providers:azure-gpt35:Endpoint"] = "https://test.openai.azure.com/",
            ["LLMProviders:Providers:azure-gpt35:Temperature"] = "0.3",
            ["LLMProviders:Providers:azure-gpt35:MaxTokens"] = "2048",

            // Test AI agent configuration
            ["AIAgents:MaxChainOfAevatarThoughtSteps"] = "10",
            ["AIAgents:MaxReActIterations"] = "15",
            ["AIAgents:DefaultProcessingMode"] = "Auto",

            // Logging configuration
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Aevatar"] = "Debug"
        };
    }

    /// <summary>
    /// Get a service from the DI container
    /// </summary>
    public T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Get an optional service from the DI container
    /// </summary>
    public T? GetOptionalService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }

    /// <summary>
    /// Create a new scope for testing
    /// </summary>
    public IServiceScope CreateScope()
    {
        return ServiceProvider.CreateScope();
    }

    public override void Dispose()
    {
        Logger?.LogInformation("AI Test Fixture disposing");
        base.Dispose();
    }
}

/// <summary>
/// Extended test fixture for specific test scenarios
/// </summary>
public class ExtendedAITestFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        
        // Add additional test services
        services.AddSingleton<ICustomTestService, CustomTestService>();
    }
}

// Example custom service for testing
public interface ICustomTestService
{
    string GetTestData();
}

public class CustomTestService : ICustomTestService
{
    private readonly ILogger<CustomTestService> _logger;

    public CustomTestService(ILogger<CustomTestService> logger)
    {
        _logger = logger;
    }

    public string GetTestData()
    {
        _logger.LogDebug("Getting test data");
        return "Test data from custom service";
    }
}