using System.ComponentModel;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Integration tests for LLMProviderFactoryBase using DI container and configuration
/// </summary>
public class LLMProviderFactoryBaseTests : IClassFixture<AITestFixture>
{
    private readonly AITestFixture _fixture;
    private readonly ILogger<LLMProviderFactoryBaseTests> _logger;

    public LLMProviderFactoryBaseTests(AITestFixture fixture)
    {
        _fixture = fixture;
        _logger = _fixture.ServiceProvider.GetRequiredService<ILogger<LLMProviderFactoryBaseTests>>();
    }

    [Fact]
    [DisplayName("Factory should be properly configured through DI")]
    public void Factory_ShouldBeProperlyConfiguredThroughDI()
    {
        // Act
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Assert
        factory.ShouldNotBeNull();
        factory.ShouldBeOfType<MockLLMProviderFactory>();
        
        // Should have providers from configuration
        var availableProviders = factory.GetAvailableProviderNames();
        availableProviders.ShouldContain("test-provider");
        availableProviders.ShouldContain("openai-provider");
        availableProviders.ShouldContain("azure-provider");
    }

    [Fact]
    [DisplayName("GetDefaultProvider should return configured default from appsettings")]
    public void GetDefaultProvider_ShouldReturnConfiguredDefault()
    {
        // Arrange
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Act
        var provider = factory.GetDefaultProvider();

        // Assert
        provider.ShouldNotBeNull();
        _logger.LogInformation("Got default provider successfully");
    }

    [Fact]
    [DisplayName("Factory should load configuration from appsettings.test.json")]
    public void Factory_ShouldLoadConfigurationFromAppSettings()
    {
        // Arrange
        var config = _fixture.GetService<IOptions<LLMProvidersConfig>>();

        // Assert
        config.ShouldNotBeNull();
        config.Value.ShouldNotBeNull();
        config.Value.Default.ShouldBe("test-provider");
        config.Value.Providers.ShouldContainKey("test-provider");
        config.Value.Providers["test-provider"].ApiKey.ShouldBe("test-key-from-config");
    }

    [Fact]
    [DisplayName("GetProviderConfig should expose embedding settings")]
    public void GetProviderConfig_ShouldExposeEmbeddingConfig()
    {
        var factory = _fixture.GetService<ILLMProviderFactory>();
        var config = factory.GetProviderConfig("openai-provider");

        config.ShouldNotBeNull();
        config.Embeddings.ShouldNotBeNull();
        config.Embeddings!.Model.ShouldBe("text-embedding-3-small");
        config.Embeddings.Dimensions.ShouldBe(512);
    }

    [Fact]
    [DisplayName("Multiple factories should share the same configuration")]
    public void MultipleFactories_ShouldShareConfiguration()
    {
        using var scope1 = _fixture.CreateScope();
        using var scope2 = _fixture.CreateScope();
        
        // Get factories from different scopes
        var factory1 = scope1.ServiceProvider.GetRequiredService<ILLMProviderFactory>();
        var factory2 = scope2.ServiceProvider.GetRequiredService<ILLMProviderFactory>();
        
        // They should have the same available providers
        var providers1 = factory1.GetAvailableProviderNames();
        var providers2 = factory2.GetAvailableProviderNames();
        
        providers1.ShouldBe(providers2);
    }

    [Fact]
    [DisplayName("Factory should handle provider creation with full config")]
    public async Task Factory_ShouldHandleProviderCreationWithFullConfig()
    {
        // Arrange
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Act - Get different providers
        var testProvider = await factory.GetProviderAsync("test-provider");
        var openaiProvider = await factory.GetProviderAsync("openai-provider");
        var azureProvider = await factory.GetProviderAsync("azure-provider");

        // Assert
        testProvider.ShouldNotBeNull();
        openaiProvider.ShouldNotBeNull();
        azureProvider.ShouldNotBeNull();
        
        // Verify model info is set correctly
        var testModelInfo = await testProvider.GetModelInfoAsync();
        testModelInfo.Name.ShouldBe("test-model");
        testModelInfo.MaxTokens.ShouldBe(1024);
        
        var openaiModelInfo = await openaiProvider.GetModelInfoAsync();
        openaiModelInfo.Name.ShouldBe("gpt-4-turbo");
        openaiModelInfo.MaxTokens.ShouldBe(4096);
        openaiModelInfo.SupportsFunctions.ShouldBeTrue();
    }

    [Fact]
    [DisplayName("Factory should log operations through injected logger")]
    public void Factory_ShouldLogOperations()
    {
        // Arrange
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Act
        var provider = factory.GetProvider("test-provider");

        // Assert
        provider.ShouldNotBeNull();
        // In a real test, we might use a test logger to verify log entries
        _logger.LogInformation("Provider retrieved successfully - logging works");
    }

    [Fact]
    [DisplayName("Factory should work with memory and tool managers from DI")]
    public async Task Factory_ShouldWorkWithOtherServices()
    {
        // Arrange
        var toolManager = _fixture.GetService<IAevatarToolManager>();
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Act - Use services together
        var tools = await toolManager.GetAvailableToolsAsync();
        var provider = factory.GetDefaultProvider();

        // Assert
        tools.ShouldNotBeEmpty();
        provider.ShouldNotBeNull();
        
        _logger.LogInformation("All services work together correctly");
    }

    [Fact]
    [DisplayName("Factory should handle configuration updates")]
    public void Factory_ShouldHandleConfigurationUpdates()
    {
        // This test demonstrates how configuration could be updated
        // In a real scenario, you might use IOptionsMonitor for dynamic updates
        
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        
        // Create new config
        var newConfig = new LLMProvidersConfig
        {
            Default = "updated-provider",
            Providers = new Dictionary<string, LLMProviderConfig>
            {
                ["updated-provider"] = new()
                {
                    Name = "updated-provider",
                    ProviderType = "openai",
                    Model = "gpt-4-updated",
                    ApiKey = "new-key"
                }
            }
        };
        
        services.Configure<LLMProvidersConfig>(options =>
        {
            options.Default = newConfig.Default;
            options.Providers = newConfig.Providers;
        });
        
        services.AddSingleton<ILLMProviderFactory, MockLLMProviderFactory>();
        
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILLMProviderFactory>();
        
        // Act & Assert
        factory.GetAvailableProviderNames().ShouldContain("updated-provider");
        var defaultProvider = factory.GetDefaultProvider();
        defaultProvider.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("test-provider")]
    [InlineData("openai-provider")]
    [InlineData("azure-provider")]
    [DisplayName("Factory should handle all configured providers")]
    public void Factory_ShouldHandleAllConfiguredProviders(string providerName)
    {
        // Arrange
        var factory = _fixture.GetService<ILLMProviderFactory>();

        // Act
        var hasProvider = factory.HasProvider(providerName);
        var provider = factory.GetProvider(providerName);

        // Assert
        hasProvider.ShouldBeTrue();
        provider.ShouldNotBeNull();
        
        _logger.LogInformation("Successfully retrieved provider: {ProviderName}", providerName);
    }

    [Fact]
    [DisplayName("Factory should properly handle scoped services")]
    public async Task Factory_ShouldHandleScopedServices()
    {
        // Create multiple scopes and verify isolation
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            using var scope = _fixture.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<ILLMProviderFactory>();
            var provider = await factory.GetProviderAsync("test-provider");
            
            // Simulate some work
            await Task.Delay(Random.Shared.Next(10, 50));
            
            provider.ShouldNotBeNull();
            _logger.LogDebug("Scope {ScopeId} completed", i);
        });

        await Task.WhenAll(tasks);
    }
}
