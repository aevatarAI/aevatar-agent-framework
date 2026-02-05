using System.Net;
using Aevatar.VibeResearching.UserProviders.Application.Services;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Volo.Abp.Users;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Services;

/// <summary>
/// Unit tests for UserProviderAppService.
/// Tests business rules, validation, ownership isolation, and default management.
/// </summary>
public sealed class UserProviderAppServiceTests
{
    private readonly Mock<IUserLlmProviderRepository> _providerRepo = new();
    private readonly Mock<IUserCodexTokenRepository> _tokenRepo = new();
    private readonly Mock<IUserProviderEncryptionService> _encryption = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ILogger<UserProviderAppService>> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private UserProviderAppService CreateService()
    {
        _currentUser.Setup(x => x.Id).Returns(_userId);
        _encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns("encrypted-value");
        _encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns("decrypted-value");

        // Create a mock-backed connectivity tester for tests that do not exercise HTTP
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler.Object));
        var tester = new ProviderConnectivityTester(
            mockFactory.Object,
            NullLogger<ProviderConnectivityTester>.Instance);

        return new UserProviderAppService(
            _providerRepo.Object,
            _tokenRepo.Object,
            _encryption.Object,
            tester,
            _currentUser.Object,
            _logger.Object);
    }

    [Fact]
    public async Task GetListAsync_ReturnsUserProviders()
    {
        // Arrange
        var service = CreateService();
        var providers = new List<UserLlmProvider>
        {
            new()
            {
                Id = "1", UserId = _userId, Name = "Test", ProviderType = "OpenAI",
                DefaultModel = "gpt-4o", EncryptedApiKey = "enc", IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            }
        };
        _providerRepo.Setup(x => x.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);

        // Act
        var result = await service.GetListAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("Test", result[0].Name);
        Assert.True(result[0].IsDefault);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_CreatesProvider()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "My OpenAI",
            ProviderType = "OpenAI",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o",
            IsDefault = false
        };

        _providerRepo.Setup(x => x.GetCountByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _providerRepo.Setup(x => x.GetByUserAndNameAsync(_userId, "My OpenAI", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _providerRepo.Setup(x => x.InsertAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => { p.Id = "new-id"; return p; });

        // Act
        var result = await service.CreateAsync(input);

        // Assert
        Assert.Equal("new-id", result.Id);
        Assert.Equal("My OpenAI", result.Name);
        _encryption.Verify(x => x.Encrypt("sk-test123456789012345678"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "My OpenAI",
            ProviderType = "OpenAI",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o"
        };

        _providerRepo.Setup(x => x.GetCountByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _providerRepo.Setup(x => x.GetByUserAndNameAsync(_userId, "My OpenAI", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserLlmProvider { Id = "existing" });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(input));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ExceedsLimit_Throws()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "My OpenAI",
            ProviderType = "OpenAI",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o"
        };

        _providerRepo.Setup(x => x.GetCountByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20); // At limit

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(input));
        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_InvalidName_Throws()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "Invalid@Name!",
            ProviderType = "OpenAI",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(input));
    }

    [Fact]
    public async Task CreateAsync_UnsupportedProviderType_Throws()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "My Provider",
            ProviderType = "UnsupportedType",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(input));
    }

    [Fact]
    public async Task CreateAsync_SetDefault_UnsetsExistingDefault()
    {
        // Arrange
        var service = CreateService();
        var input = new CreateUserProviderDto
        {
            Name = "My OpenAI",
            ProviderType = "OpenAI",
            ApiKey = "sk-test123456789012345678",
            DefaultModel = "gpt-4o",
            IsDefault = true
        };

        _providerRepo.Setup(x => x.GetCountByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _providerRepo.Setup(x => x.GetByUserAndNameAsync(_userId, "My OpenAI", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _providerRepo.Setup(x => x.InsertAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => { p.Id = "id"; return p; });

        // Act
        await service.CreateAsync(input);

        // Assert -- should unset previous default
        _providerRepo.Verify(x => x.UnsetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_DefaultProvider_PromotesOldest()
    {
        // Arrange
        var service = CreateService();
        var provider = new UserLlmProvider
        {
            Id = "1", UserId = _userId, Name = "Default", IsDefault = true,
            ProviderType = "OpenAI", DefaultModel = "gpt-4o"
        };
        var oldest = new UserLlmProvider
        {
            Id = "2", UserId = _userId, Name = "Oldest",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o"
        };

        _providerRepo.Setup(x => x.GetByIdAndUserAsync("1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        _providerRepo.Setup(x => x.GetOldestByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldest);
        _providerRepo.Setup(x => x.UpdateAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => p);

        // Act
        await service.DeleteAsync("1");

        // Assert
        _providerRepo.Verify(x => x.DeleteAsync("1", It.IsAny<CancellationToken>()), Times.Once);
        _providerRepo.Verify(x => x.UpdateAsync(
            It.Is<UserLlmProvider>(p => p.Id == "2" && p.IsDefault),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CodexProvider_DeletesTokens()
    {
        // Arrange
        var service = CreateService();
        var provider = new UserLlmProvider
        {
            Id = "1", UserId = _userId, Name = "Codex", IsCodexOAuth = true,
            ProviderType = "CodexOAuth", DefaultModel = "gpt-4o"
        };

        _providerRepo.Setup(x => x.GetByIdAndUserAsync("1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // Act
        await service.DeleteAsync("1");

        // Assert
        _tokenRepo.Verify(x => x.DeleteByUserAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var service = CreateService();
        _providerRepo.Setup(x => x.GetByIdAndUserAsync("nonexistent", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync("nonexistent"));
    }

    [Fact]
    public async Task SetDefaultAsync_ValidProvider_UnsetsOldAndSetsNew()
    {
        // Arrange
        var service = CreateService();
        var provider = new UserLlmProvider
        {
            Id = "1", UserId = _userId, Name = "Test",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o"
        };

        _providerRepo.Setup(x => x.GetByIdAndUserAsync("1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        _providerRepo.Setup(x => x.UpdateAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => p);

        // Act
        await service.SetDefaultAsync(new SetDefaultProviderDto { ProviderId = "1" });

        // Assert
        _providerRepo.Verify(x => x.UnsetDefaultByUserAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _providerRepo.Verify(x => x.UpdateAsync(
            It.Is<UserLlmProvider>(p => p.IsDefault),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RetainsExistingKeyWhenNull()
    {
        // Arrange
        var service = CreateService();
        var provider = new UserLlmProvider
        {
            Id = "1", UserId = _userId, Name = "Test",
            ProviderType = "OpenAI", DefaultModel = "gpt-4o",
            EncryptedApiKey = "original-encrypted"
        };

        _providerRepo.Setup(x => x.GetByIdAndUserAsync("1", _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);
        _providerRepo.Setup(x => x.UpdateAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => p);

        var input = new UpdateUserProviderDto { DefaultModel = "gpt-4o-mini" };

        // Act
        await service.UpdateAsync("1", input);

        // Assert -- encryption should NOT be called since ApiKey is null
        _encryption.Verify(x => x.Encrypt(It.IsAny<string>()), Times.Never);
        Assert.Equal("original-encrypted", provider.EncryptedApiKey);
    }
}
