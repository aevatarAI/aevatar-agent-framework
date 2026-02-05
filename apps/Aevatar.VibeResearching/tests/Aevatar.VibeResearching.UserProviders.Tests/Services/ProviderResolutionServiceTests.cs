using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Resolution;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Services;

/// <summary>
/// Unit tests for the 3-layer provider resolution service.
/// Tests all layers, fallback scenarios, and invalid namespaces.
/// </summary>
public sealed class ProviderResolutionServiceTests
{
    private readonly Mock<IUserLlmProviderRepository> _providerRepo = new();
    private readonly Mock<IUserCodexTokenRepository> _tokenRepo = new();
    private readonly Mock<IAgentProvidersRepository> _agentProvidersRepo = new();
    private readonly Mock<IUserProviderEncryptionService> _encryption = new();
    private readonly Mock<ILLMProviderFactory> _factory = new();
    private readonly Mock<ICodexOAuthService> _codexService = new();
    private readonly Mock<ILogger<ProviderResolutionService>> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private ProviderResolutionService CreateService()
    {
        _encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns("decrypted-api-key");

        var codexOptions = Options.Create(new CodexOAuthOptions
        {
            CodexApiBaseUrl = "https://chatgpt.com/backend-api/codex/responses"
        });

        return new ProviderResolutionService(
            _providerRepo.Object,
            _tokenRepo.Object,
            _agentProvidersRepo.Object,
            _encryption.Object,
            _factory.Object,
            _codexService.Object,
            codexOptions,
            _logger.Object);
    }

    [Fact]
    public async Task ResolveAsync_Layer1_UserMapping_ReturnsUserProvider()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(1, "2026-01-01", new Dictionary<string, string>
        {
            ["researcher"] = "user:provider1"
        });
        var provider = new UserLlmProvider
        {
            Id = "provider1", UserId = _userId, Name = "My OpenAI",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o",
            EncryptedApiKey = "encrypted"
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _providerRepo.Setup(x => x.GetByIdAndUserAsync("provider1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert
        Assert.Equal("session-agent-mapping", result.ResolutionSource);
        Assert.Equal("user:provider1", result.ProviderNamespace);
        Assert.Equal("OpenAI", result.ProviderType);
        Assert.Equal("gpt-4o", result.Model);
    }

    [Fact]
    public async Task ResolveAsync_Layer1_PlatformMapping_ReturnsPlatformProvider()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(1, "2026-01-01", new Dictionary<string, string>
        {
            ["researcher"] = "platform:openai-gpt4"
        });
        var platformConfig = new LLMProviderConfig
        {
            Name = "openai-gpt4", ProviderType = "OpenAI", Model = "gpt-4"
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _factory.Setup(x => x.HasProvider("openai-gpt4")).Returns(true);
        _factory.Setup(x => x.GetProviderConfig("openai-gpt4")).Returns(platformConfig);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert
        Assert.Equal("session-agent-mapping", result.ResolutionSource);
        Assert.Equal("platform:openai-gpt4", result.ProviderNamespace);
    }

    [Fact]
    public async Task ResolveAsync_Layer2_UserDefault_WhenNoSessionMapping()
    {
        // Arrange
        var service = CreateService();
        var emptySnapshot = new AgentProvidersSnapshot(0, "", new Dictionary<string, string>());
        var defaultProvider = new UserLlmProvider
        {
            Id = "default1", UserId = _userId, Name = "Default",
            ProviderType = "Anthropic", DefaultModel = "claude-sonnet-4-20250514",
            EncryptedApiKey = "enc", IsDefault = true
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptySnapshot);
        _providerRepo.Setup(x => x.GetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultProvider);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert
        Assert.Equal("user-default", result.ResolutionSource);
        Assert.Equal("Anthropic", result.ProviderType);
    }

    [Fact]
    public async Task ResolveAsync_Layer3_PlatformDefault_WhenNoUserProvider()
    {
        // Arrange
        var service = CreateService();
        var emptySnapshot = new AgentProvidersSnapshot(0, "", new Dictionary<string, string>());
        var platformConfig = new LLMProviderConfig
        {
            Name = "openai-gpt4", ProviderType = "OpenAI", Model = "gpt-4"
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptySnapshot);
        _providerRepo.Setup(x => x.GetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _factory.Setup(x => x.GetDefaultProviderConfig()).Returns(platformConfig);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert
        Assert.Equal("platform-default", result.ResolutionSource);
    }

    [Fact]
    public async Task ResolveAsync_InvalidNamespace_FallsThrough()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(1, "2026-01-01", new Dictionary<string, string>
        {
            ["researcher"] = "user:nonexistent"
        });
        var platformConfig = new LLMProviderConfig
        {
            Name = "fallback", ProviderType = "OpenAI", Model = "gpt-4"
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _providerRepo.Setup(x => x.GetByIdAndUserAsync("nonexistent", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _providerRepo.Setup(x => x.GetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _factory.Setup(x => x.GetDefaultProviderConfig()).Returns(platformConfig);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert - falls through to platform default
        Assert.Equal("platform-default", result.ResolutionSource);
    }

    [Fact]
    public async Task ResolveAsync_AllLayersMiss_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = CreateService();
        var emptySnapshot = new AgentProvidersSnapshot(0, "", new Dictionary<string, string>());

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptySnapshot);
        _providerRepo.Setup(x => x.GetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _factory.Setup(x => x.GetDefaultProviderConfig())
            .Throws(new KeyNotFoundException("No default provider"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolveAsync("session1", "researcher", _userId));
    }

    [Fact]
    public async Task ResolveAsync_UnprefixedBackwardCompat_TreatsAsPlatform()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(1, "", new Dictionary<string, string>
        {
            ["researcher"] = "openai-gpt4" // No prefix = backward compat
        });
        var platformConfig = new LLMProviderConfig
        {
            Name = "openai-gpt4", ProviderType = "OpenAI", Model = "gpt-4"
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _factory.Setup(x => x.HasProvider("openai-gpt4")).Returns(true);
        _factory.Setup(x => x.GetProviderConfig("openai-gpt4")).Returns(platformConfig);

        // Act
        var result = await service.ResolveAsync("session1", "researcher", _userId);

        // Assert
        Assert.Equal("session-agent-mapping", result.ResolutionSource);
        Assert.Equal("platform:openai-gpt4", result.ProviderNamespace);
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_MergesUserAndPlatform()
    {
        // Arrange
        var service = CreateService();
        var userProviders = new List<UserLlmProvider>
        {
            new() { Id = "1", UserId = _userId, Name = "My OpenAI",
                    ProviderType = "OpenAI", DefaultModel = "gpt-4o", IsDefault = true }
        };
        _providerRepo.Setup(x => x.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProviders);
        _factory.Setup(x => x.GetAvailableProviderNames())
            .Returns(new List<string> { "platform-openai" });
        _factory.Setup(x => x.GetProviderConfig("platform-openai"))
            .Returns(new LLMProviderConfig
            {
                Name = "platform-openai", ProviderType = "OpenAI", Model = "gpt-4"
            });

        // Act
        var result = await service.GetAvailableProvidersAsync(_userId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Source == "user" && p.Name == "My OpenAI");
        Assert.Contains(result, p => p.Source == "platform" && p.Name == "platform-openai");
    }
}
