using Aevatar.VibeResearching.UserProviders.Application.Services;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Services;
using Moq;
using Volo.Abp.Users;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Codex;

/// <summary>
/// Unit tests for CodexOAuthAppService covering input validation
/// and delegation to ICodexOAuthService.
/// </summary>
public sealed class CodexOAuthAppServiceTests
{
    private readonly Mock<ICodexOAuthService> _codexService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _userId = Guid.NewGuid();

    private CodexOAuthAppService CreateService()
    {
        _currentUser.Setup(x => x.Id).Returns(_userId);
        return new CodexOAuthAppService(_codexService.Object, _currentUser.Object);
    }

    [Fact]
    public async Task InitiateAsync_ValidInput_DelegatesToCodexService()
    {
        // Arrange
        var service = CreateService();
        var input = new CodexInitiateRequestDto { RedirectUri = "https://app.test.com/callback" };
        _codexService.Setup(x => x.InitiateAsync(_userId, "https://app.test.com/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodexInitiateResult("https://auth.test.com/authorize?state=abc", "abc"));

        // Act
        var result = await service.InitiateAsync(input);

        // Assert
        Assert.Equal("https://auth.test.com/authorize?state=abc", result.AuthUrl);
        Assert.Equal("abc", result.State);
    }

    [Fact]
    public async Task InitiateAsync_EmptyRedirectUri_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var input = new CodexInitiateRequestDto { RedirectUri = "" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.InitiateAsync(input));
    }

    [Fact]
    public async Task HandleCallbackAsync_EmptyCode_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var input = new CodexCallbackDto { Code = "", State = "valid-state", RedirectUri = "https://redirect" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleCallbackAsync(input));
    }

    [Fact]
    public async Task HandleCallbackAsync_EmptyState_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var input = new CodexCallbackDto { Code = "valid-code", State = "", RedirectUri = "https://redirect" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.HandleCallbackAsync(input));
    }

    [Fact]
    public async Task GetStatusAsync_DelegatesToCodexService()
    {
        // Arrange
        var service = CreateService();
        _codexService.Setup(x => x.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodexConnectionInfo(true, "user@test.com", DateTimeOffset.UtcNow, "provider-id"));

        // Act
        var result = await service.GetStatusAsync();

        // Assert
        Assert.True(result.Connected);
        Assert.Equal("user@test.com", result.Email);
        Assert.Equal("provider-id", result.ProviderId);
    }

    [Fact]
    public async Task DisconnectAsync_DelegatesToCodexService()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.DisconnectAsync();

        // Assert
        _codexService.Verify(x => x.DisconnectAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_UnauthenticatedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _currentUser.Setup(x => x.Id).Returns((Guid?)null);
        var service = new CodexOAuthAppService(_codexService.Object, _currentUser.Object);
        var input = new CodexInitiateRequestDto { RedirectUri = "https://redirect" };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.InitiateAsync(input));
    }
}
