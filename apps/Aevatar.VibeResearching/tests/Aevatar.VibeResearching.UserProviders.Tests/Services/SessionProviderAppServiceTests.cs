using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.UserProviders.Application.Services;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Aevatar.VibeResearching.UserProviders.ValueObjects;
using Moq;
using Volo.Abp.Users;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Services;

/// <summary>
/// Unit tests for SessionProviderAppService covering agent-provider
/// mapping CRUD, available providers aggregation, and namespace validation.
/// </summary>
public sealed class SessionProviderAppServiceTests
{
    private readonly Mock<IAgentProvidersRepository> _agentProvidersRepo = new();
    private readonly Mock<IProviderResolutionService> _resolutionService = new();
    private readonly Mock<IUserLlmProviderRepository> _providerRepo = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _userId = Guid.NewGuid();

    private SessionProviderAppService CreateService()
    {
        _currentUser.Setup(x => x.Id).Returns(_userId);

        return new SessionProviderAppService(
            _agentProvidersRepo.Object,
            _resolutionService.Object,
            _providerRepo.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task GetAgentProvidersAsync_NoSnapshot_ReturnsEmptyResponse()
    {
        // Arrange
        var service = CreateService();
        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentProvidersSnapshot?)null);

        // Act
        var result = await service.GetAgentProvidersAsync("session1");

        // Assert
        Assert.Equal(0, result.Version);
        Assert.Empty(result.Map);
        Assert.Equal(string.Empty, result.UpdatedAt);
    }

    [Fact]
    public async Task GetAgentProvidersAsync_WithMappings_ReturnsEnrichedMap()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(2, "2026-01-01T00:00:00Z",
            new Dictionary<string, string>
            {
                ["researcher"] = "user:provider1",
                ["coder"] = "platform:openai"
            });
        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var provider = new UserLlmProvider
        {
            Id = "provider1", UserId = _userId, Name = "My Provider",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o"
        };
        _providerRepo.Setup(x => x.GetByIdAndUserAsync("provider1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // Act
        var result = await service.GetAgentProvidersAsync("session1");

        // Assert
        Assert.Equal(2, result.Version);
        Assert.Equal("2026-01-01T00:00:00Z", result.UpdatedAt);
        Assert.True(result.Map.ContainsKey("researcher"));
        Assert.Equal("My Provider", result.Map["researcher"].Name);
        // Platform provider is resolved via namespace
        Assert.True(result.Map.ContainsKey("coder"));
        Assert.Equal("openai", result.Map["coder"].Name);
    }

    [Fact]
    public async Task UpdateAgentProvidersAsync_ValidUserNamespace_PersistsMappings()
    {
        // Arrange
        var service = CreateService();
        var input = new UpdateAgentProvidersDto
        {
            Map = new Dictionary<string, string?>
            {
                ["researcher"] = "user:provider1"
            }
        };

        var provider = new UserLlmProvider
        {
            Id = "provider1", UserId = _userId, Name = "My OpenAI",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o"
        };
        _providerRepo.Setup(x => x.GetByIdAndUserAsync("provider1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // After upsert, return the updated snapshot
        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProvidersSnapshot(1, "2026-01-01", new Dictionary<string, string>
            {
                ["researcher"] = "user:provider1"
            }));

        // Act
        var result = await service.UpdateAgentProvidersAsync("session1", input);

        // Assert
        _agentProvidersRepo.Verify(x => x.UpsertAsync("session1", "researcher", "user:provider1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, result.Version);
    }

    [Fact]
    public async Task UpdateAgentProvidersAsync_NonexistentUserProvider_ThrowsKeyNotFoundException()
    {
        // Arrange
        var service = CreateService();
        var input = new UpdateAgentProvidersDto
        {
            Map = new Dictionary<string, string?>
            {
                ["researcher"] = "user:nonexistent"
            }
        };

        _providerRepo.Setup(x => x.GetByIdAndUserAsync("nonexistent", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.UpdateAgentProvidersAsync("session1", input));
    }

    [Fact]
    public async Task UpdateAgentProvidersAsync_NullValue_RemovesMapping()
    {
        // Arrange
        var service = CreateService();
        var input = new UpdateAgentProvidersDto
        {
            Map = new Dictionary<string, string?>
            {
                ["researcher"] = null
            }
        };

        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProvidersSnapshot(1, "", new Dictionary<string, string>()));

        // Act
        await service.UpdateAgentProvidersAsync("session1", input);

        // Assert -- null/empty values are treated as removal (set to "default")
        _agentProvidersRepo.Verify(
            x => x.UpsertAsync("session1", "researcher", "default", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAvailableProvidersAsync_ReturnsProviderList()
    {
        // Arrange
        var service = CreateService();
        var providers = new List<AvailableProvider>
        {
            new("user:1", "My OpenAI", "OpenAI", "gpt-4o", "user", true),
            new("platform:default", "Default", "OpenAI", "gpt-4", "platform", false)
        };
        _resolutionService.Setup(x => x.GetAvailableProvidersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);

        // Act
        var result = await service.GetAvailableProvidersAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Source == "user" && p.IsDefault);
        Assert.Contains(result, p => p.Source == "platform");
    }

    [Fact]
    public async Task GetAventProvidersAsync_EmptyMap_ReturnsEmptyEnrichedMap()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(0, "", new Dictionary<string, string>());
        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await service.GetAgentProvidersAsync("session1");

        // Assert
        Assert.Equal(0, result.Version);
        Assert.Empty(result.Map);
    }

    [Fact]
    public async Task GetAgentProvidersAsync_WhitespaceNamespaceValue_SkipsEntry()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new AgentProvidersSnapshot(1, "2026-01-01",
            new Dictionary<string, string>
            {
                ["researcher"] = "   " // whitespace-only
            });
        _agentProvidersRepo.Setup(x => x.LoadAsync("session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await service.GetAgentProvidersAsync("session1");

        // Assert
        Assert.Empty(result.Map);
    }
}
