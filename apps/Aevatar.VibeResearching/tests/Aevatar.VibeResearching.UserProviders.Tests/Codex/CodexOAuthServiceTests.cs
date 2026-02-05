using System.Net;
using System.Text.Json;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Codex;

/// <summary>
/// Unit tests for CodexOAuthService covering the full OAuth PKCE lifecycle:
/// initiation, callback, state validation, token refresh, and disconnect cascade.
/// </summary>
public sealed class CodexOAuthServiceTests
{
    private readonly Mock<IUserProviderEncryptionService> _encryption = new();
    private readonly Mock<IUserCodexTokenRepository> _tokenRepo = new();
    private readonly Mock<IUserLlmProviderRepository> _providerRepo = new();
    private readonly Mock<ILogger<CodexOAuthService>> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private static CodexOAuthOptions DefaultOptions => new()
    {
        ClientId = "test-client-id",
        AuthorizationEndpoint = "https://auth.test.com/authorize",
        TokenEndpoint = "https://auth.test.com/oauth/token",
        Scopes = "openid profile email",
        CodexApiBaseUrl = "https://api.test.com/codex"
    };

    private PkceStateStore CreateStateStore()
    {
        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        return new PkceStateStore(cache);
    }

    private (CodexOAuthService Service, Mock<HttpMessageHandler> Handler) CreateServiceWithMockHttp(
        PkceStateStore? stateStore = null)
    {
        _encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns("encrypted-value");
        _encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns("decrypted-value");

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        httpClient.BaseAddress = new Uri("https://auth.test.com");

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(x => x.CreateClient("CodexOAuth")).Returns(httpClient);

        var options = Options.Create(DefaultOptions);
        var store = stateStore ?? CreateStateStore();

        var service = new CodexOAuthService(
            store, _encryption.Object, _tokenRepo.Object,
            _providerRepo.Object, mockFactory.Object,
            options, _logger.Object);

        return (service, mockHandler);
    }

    private static void SetupTokenResponse(
        Mock<HttpMessageHandler> handler, string accessToken = "test-access-token",
        string? refreshToken = "test-refresh-token", int expiresIn = 3600)
    {
        var tokenResponse = JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            id_token = (string?)null,
            expires_in = expiresIn,
            token_type = "Bearer"
        });

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tokenResponse)
            });
    }

    [Fact]
    public async Task InitiateAsync_ReturnsAuthUrlWithPkceParams()
    {
        // Arrange
        var (service, _) = CreateServiceWithMockHttp();
        var redirectUri = "https://app.test.com/callback";

        // Act
        var result = await service.InitiateAsync(_userId, redirectUri);

        // Assert
        Assert.NotNull(result.AuthUrl);
        Assert.Contains("client_id=test-client-id", result.AuthUrl);
        Assert.Contains("response_type=code", result.AuthUrl);
        Assert.Contains("code_challenge_method=S256", result.AuthUrl);
        Assert.Contains("code_challenge=", result.AuthUrl);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(redirectUri)}", result.AuthUrl);
        Assert.NotEmpty(result.State);
    }

    [Fact]
    public async Task HandleCallbackAsync_ValidState_ExchangesTokensAndCreatesProvider()
    {
        // Arrange
        var stateStore = CreateStateStore();
        var (service, handler) = CreateServiceWithMockHttp(stateStore);
        SetupTokenResponse(handler);

        _providerRepo.Setup(x => x.GetCodexByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider?)null);
        _providerRepo.Setup(x => x.InsertAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => { p.Id = "new-codex-id"; return p; });

        // First initiate to get a valid state
        var initResult = await service.InitiateAsync(_userId, "https://app.test.com/callback");

        // Act
        var result = await service.HandleCallbackAsync(
            _userId, "auth-code-123", initResult.State, "https://app.test.com/callback");

        // Assert
        Assert.Equal("connected", result.Status);
        Assert.Equal("new-codex-id", result.ProviderId);
        _tokenRepo.Verify(x => x.UpsertAsync(It.IsAny<UserCodexToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _providerRepo.Verify(x => x.InsertAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackAsync_InvalidState_ThrowsInvalidOperationException()
    {
        // Arrange
        var (service, handler) = CreateServiceWithMockHttp();
        SetupTokenResponse(handler);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.HandleCallbackAsync(_userId, "code", "invalid-state", "https://redirect"));
    }

    [Fact]
    public async Task HandleCallbackAsync_UserIdMismatch_ThrowsInvalidOperationException()
    {
        // Arrange
        var stateStore = CreateStateStore();
        var (service, handler) = CreateServiceWithMockHttp(stateStore);
        SetupTokenResponse(handler);

        // Initiate as one user
        var initResult = await service.InitiateAsync(_userId, "https://redirect");

        // Act & Assert -- try to use the state as a different user
        var differentUser = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.HandleCallbackAsync(differentUser, "code", initResult.State, "https://redirect"));
        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public async Task HandleCallbackAsync_ExistingCodexProvider_UpdatesInsteadOfCreating()
    {
        // Arrange
        var stateStore = CreateStateStore();
        var (service, handler) = CreateServiceWithMockHttp(stateStore);
        SetupTokenResponse(handler);

        var existingProvider = new UserLlmProvider
        {
            Id = "existing-codex", UserId = _userId, Name = "Codex (ChatGPT)",
            ProviderType = "CodexOAuth", IsCodexOAuth = true, DefaultModel = "gpt-4o"
        };
        _providerRepo.Setup(x => x.GetCodexByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProvider);
        _providerRepo.Setup(x => x.UpdateAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserLlmProvider p, CancellationToken _) => p);

        var initResult = await service.InitiateAsync(_userId, "https://redirect");

        // Act
        var result = await service.HandleCallbackAsync(
            _userId, "auth-code", initResult.State, "https://redirect");

        // Assert
        Assert.Equal("connected", result.Status);
        Assert.Equal("existing-codex", result.ProviderId);
        _providerRepo.Verify(x => x.UpdateAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()), Times.Once);
        _providerRepo.Verify(x => x.InsertAsync(It.IsAny<UserLlmProvider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleCallbackAsync_TokenExchangeFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stateStore = CreateStateStore();
        var (service, handler) = CreateServiceWithMockHttp(stateStore);

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\"}")
            });

        var initResult = await service.InitiateAsync(_userId, "https://redirect");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.HandleCallbackAsync(_userId, "bad-code", initResult.State, "https://redirect"));
    }

    [Fact]
    public async Task GetStatusAsync_NoToken_ReturnsDisconnected()
    {
        // Arrange
        var (service, _) = CreateServiceWithMockHttp();
        _tokenRepo.Setup(x => x.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserCodexToken?)null);

        // Act
        var status = await service.GetStatusAsync(_userId);

        // Assert
        Assert.False(status.Connected);
        Assert.Null(status.Email);
    }

    [Fact]
    public async Task GetStatusAsync_WithToken_ReturnsConnected()
    {
        // Arrange
        var (service, _) = CreateServiceWithMockHttp();
        var token = new UserCodexToken
        {
            UserId = _userId, Email = "user@test.com",
            ConnectedAt = DateTimeOffset.UtcNow
        };
        var provider = new UserLlmProvider { Id = "codex-id", UserId = _userId };

        _tokenRepo.Setup(x => x.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _providerRepo.Setup(x => x.GetCodexByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

        // Act
        var status = await service.GetStatusAsync(_userId);

        // Assert
        Assert.True(status.Connected);
        Assert.Equal("user@test.com", status.Email);
        Assert.Equal("codex-id", status.ProviderId);
    }

    [Fact]
    public async Task DisconnectAsync_DeletesTokensAndCodexProvider()
    {
        // Arrange
        var (service, _) = CreateServiceWithMockHttp();
        var codexProvider = new UserLlmProvider
        {
            Id = "codex-to-delete", UserId = _userId, IsCodexOAuth = true
        };
        _providerRepo.Setup(x => x.GetCodexByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codexProvider);

        // Act
        await service.DisconnectAsync(_userId);

        // Assert
        _tokenRepo.Verify(x => x.DeleteByUserAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _providerRepo.Verify(x => x.DeleteAsync("codex-to-delete", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsWithoutRefresh()
    {
        // Arrange
        var (service, handler) = CreateServiceWithMockHttp();
        var token = new UserCodexToken
        {
            UserId = _userId,
            EncryptedAccessToken = "encrypted-access",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _tokenRepo.Setup(x => x.GetByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _encryption.Setup(x => x.Decrypt("encrypted-access")).Returns("valid-access-token");

        // Act
        var result = await service.GetValidAccessTokenAsync(_userId);

        // Assert
        Assert.Equal("valid-access-token", result);
        // Verify no HTTP calls were made (no refresh needed)
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
