using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.VibeResearching.UserProviders.Constants;
using Aevatar.VibeResearching.UserProviders.Entities;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;

/// <summary>
/// Manages the Codex OAuth PKCE flow lifecycle.
/// Handles initiation, callback, token refresh, and disconnection.
/// </summary>
public sealed class CodexOAuthService : ICodexOAuthService
{
    private readonly PkceStateStore _stateStore;
    private readonly IUserProviderEncryptionService _encryption;
    private readonly IUserCodexTokenRepository _tokenRepo;
    private readonly IUserLlmProviderRepository _providerRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CodexOAuthOptions _options;
    private readonly ILogger<CodexOAuthService> _logger;

    public CodexOAuthService(
        PkceStateStore stateStore,
        IUserProviderEncryptionService encryption,
        IUserCodexTokenRepository tokenRepo,
        IUserLlmProviderRepository providerRepo,
        IHttpClientFactory httpClientFactory,
        IOptions<CodexOAuthOptions> options,
        ILogger<CodexOAuthService> logger)
    {
        _stateStore = stateStore;
        _encryption = encryption;
        _tokenRepo = tokenRepo;
        _providerRepo = providerRepo;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string GetAuthMode() => _options.AuthMode.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public async Task<CodexInitiateResult> InitiateAsync(
        Guid userId, string redirectUri, CancellationToken ct = default)
    {
        // Generate PKCE parameters
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var state = GenerateState();

        // Store state + verifier for callback validation
        await _stateStore.StoreAsync(state, new PkceStateEntry(userId, codeVerifier, DateTimeOffset.UtcNow), ct);

        // Build authorization URL (originator + simplified flow required for Codex CLI OAuth)
        var authUrl = $"{_options.AuthorizationEndpoint}" +
                      $"?response_type=code" +
                      $"&client_id={Uri.EscapeDataString(_options.ClientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={Uri.EscapeDataString(_options.Scopes)}" +
                      $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                      $"&code_challenge_method=S256" +
                      $"&state={Uri.EscapeDataString(state)}" +
                      $"&codex_cli_simplified_flow=true" +
                      $"&originator=aevatar";

        return new CodexInitiateResult(authUrl, state);
    }

    /// <inheritdoc />
    public async Task<CodexCallbackResult> HandleCallbackAsync(
        Guid userId, string code, string state, string redirectUri, CancellationToken ct = default)
    {
        var entry = await ValidateAndConsumeStateAsync(userId, state, ct);
        var tokenResponse = await ExchangeCodeForTokensAsync(code, entry.CodeVerifier, redirectUri, ct);

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Token exchange failed: no access token received.");

        var (email, accountId) = ExtractIdTokenClaims(tokenResponse.IdToken);
        await StoreEncryptedTokensAsync(userId, tokenResponse, accountId, email, ct);

        return await UpsertCodexProviderAsync(userId, email, ct);
    }

    private async Task<PkceStateEntry> ValidateAndConsumeStateAsync(
        Guid userId, string state, CancellationToken ct)
    {
        var entry = await _stateStore.ConsumeAsync(state, ct)
            ?? throw new InvalidOperationException("Invalid or expired OAuth state.");

        if (entry.UserId != userId)
            throw new InvalidOperationException("OAuth state does not match the current user.");

        return entry;
    }

    private static (string? Email, string? AccountId) ExtractIdTokenClaims(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
            return (null, null);

        return ParseIdTokenClaims(idToken);
    }

    private async Task StoreEncryptedTokensAsync(
        Guid userId, TokenResponse tokenResponse,
        string? accountId, string? email, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600);

        var codexToken = new UserCodexToken
        {
            UserId = userId,
            EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken!),
            EncryptedRefreshToken = !string.IsNullOrEmpty(tokenResponse.RefreshToken)
                ? _encryption.Encrypt(tokenResponse.RefreshToken)
                : string.Empty,
            AccessTokenExpiresAt = expiresAt,
            ChatGptAccountId = accountId,
            Email = email,
            ConnectedAt = now,
            UpdatedAt = now
        };

        await _tokenRepo.UpsertAsync(codexToken, ct);
    }

    private async Task<CodexCallbackResult> UpsertCodexProviderAsync(
        Guid userId, string? email, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existingCodex = await _providerRepo.GetCodexByUserAsync(userId, ct);

        if (existingCodex != null)
        {
            existingCodex.UpdatedAt = now;
            existingCodex.DefaultModel = UserProviderConsts.CodexDefaultModel;
            await _providerRepo.UpdateAsync(existingCodex, ct);
            return new CodexCallbackResult("connected", email, existingCodex.Id);
        }

        var newProvider = new UserLlmProvider
        {
            UserId = userId,
            Name = UserProviderConsts.CodexProviderName,
            ProviderType = "CodexOAuth",
            EncryptedApiKey = string.Empty,
            DefaultModel = UserProviderConsts.CodexDefaultModel,
            IsDefault = false,
            IsCodexOAuth = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await _providerRepo.InsertAsync(newProvider, ct);
        return new CodexCallbackResult("connected", email, created.Id);
    }

    /// <inheritdoc />
    public async Task<CodexConnectionInfo> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var token = await _tokenRepo.GetByUserAsync(userId, ct);
        if (token == null)
            return new CodexConnectionInfo(false, null, null, null);

        var provider = await _providerRepo.GetCodexByUserAsync(userId, ct);
        return new CodexConnectionInfo(
            true,
            token.Email,
            token.ConnectedAt,
            provider?.Id);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(Guid userId, CancellationToken ct = default)
    {
        // Delete tokens
        await _tokenRepo.DeleteByUserAsync(userId, ct);

        // Delete Codex provider
        var codexProvider = await _providerRepo.GetCodexByUserAsync(userId, ct);
        if (codexProvider != null)
        {
            await _providerRepo.DeleteAsync(codexProvider.Id, ct);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetValidAccessTokenAsync(Guid userId, CancellationToken ct = default)
    {
        var semaphore = RefreshLockManager.GetOrCreate(userId);
        await semaphore.WaitAsync(ct);

        try
        {
            var token = await _tokenRepo.GetByUserAsync(userId, ct)
                ?? throw new InvalidOperationException("Codex is not connected. Please authenticate first.");

            // If token is still valid (more than 5 minutes remaining), return it
            if (token.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _encryption.Decrypt(token.EncryptedAccessToken);
            }

            // Token is expiring or expired; attempt refresh
            if (string.IsNullOrEmpty(token.EncryptedRefreshToken))
            {
                throw new InvalidOperationException("Codex token expired and no refresh token available.");
            }

            var refreshToken = _encryption.Decrypt(token.EncryptedRefreshToken);
            var refreshed = await RefreshTokenAsync(refreshToken, ct);

            if (string.IsNullOrEmpty(refreshed.AccessToken))
            {
                // Refresh failed; clean up
                await _tokenRepo.DeleteByUserAsync(userId, ct);
                throw new InvalidOperationException("Codex token refresh failed. Please reconnect.");
            }

            var now = DateTimeOffset.UtcNow;
            token.EncryptedAccessToken = _encryption.Encrypt(refreshed.AccessToken);
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                token.EncryptedRefreshToken = _encryption.Encrypt(refreshed.RefreshToken);
            }
            token.AccessTokenExpiresAt = now.AddSeconds(refreshed.ExpiresIn > 0 ? refreshed.ExpiresIn : 3600);
            token.UpdatedAt = now;

            await _tokenRepo.UpsertAsync(token, ct);
            return refreshed.AccessToken;
        }
        finally
        {
            semaphore.Release();
        }
    }

    // ================================================================
    //  Device Code Flow (OpenAI custom — NOT standard RFC 8628)
    //
    //  1. POST /api/accounts/deviceauth/usercode  → device_auth_id, user_code
    //  2. User visits /codex/device and enters user_code
    //  3. POST /api/accounts/deviceauth/token      → authorization_code, code_verifier
    //  4. POST /oauth/token (standard PKCE exchange) → access/refresh/id tokens
    // ================================================================

    /// <inheritdoc />
    public async Task<DeviceCodeInitiateResult> InitiateDeviceCodeAsync(
        Guid userId, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("CodexOAuth");
        var jsonBody = JsonSerializer.Serialize(new { client_id = _options.ClientId });
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(_options.DeviceUserCodeEndpoint, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Device code request failed: {Status} {Body}",
                response.StatusCode, TruncateForLog(responseJson));
            throw new InvalidOperationException("Failed to request device code from OpenAI.");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var deviceAuthId = root.GetProperty("device_auth_id").GetString()!;
        var userCode = root.GetProperty("user_code").GetString()!;
        var interval = root.TryGetProperty("interval", out var ivProp)
            ? ParseIntOrString(ivProp)
            : 5;

        _logger.LogInformation(
            "Device code flow initiated for user {UserId}, poll interval={Interval}s.",
            userId, interval);

        return new DeviceCodeInitiateResult(
            deviceAuthId, userCode, _options.DeviceVerificationUri, interval);
    }

    /// <inheritdoc />
    public async Task<DeviceCodePollResult> PollDeviceCodeAsync(
        Guid userId, string deviceAuthId, string userCode, CancellationToken ct = default)
    {
        // Step 3: Poll for authorization code
        HttpResponseMessage response;
        string responseJson;

        try
        {
            var client = _httpClientFactory.CreateClient("CodexOAuth");
            var pollBody = JsonSerializer.Serialize(new
            {
                device_auth_id = deviceAuthId,
                user_code = userCode
            });
            var pollContent = new StringContent(pollBody, Encoding.UTF8, "application/json");

            response = await client.PostAsync(_options.DevicePollEndpoint, pollContent, ct);
            responseJson = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Device poll HTTP request failed.");
            return new DeviceCodePollResult("pending"); // Transient — keep polling
        }

        _logger.LogDebug("Device poll response: {Status} {Body}",
            response.StatusCode, TruncateForLog(responseJson));

        if (!response.IsSuccessStatusCode)
        {
            if (IsDeviceExpired(responseJson))
                return new DeviceCodePollResult("expired");

            // Any non-success = pending (user hasn't approved yet)
            return new DeviceCodePollResult("pending");
        }

        // Success — extract authorization_code + code_verifier from response
        _logger.LogInformation("Device poll returned success. Body: {Body}",
            TruncateForLog(responseJson, 800));

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("authorization_code", out var codeProp) ||
            string.IsNullOrEmpty(codeProp.GetString()))
        {
            _logger.LogWarning("Device poll success but no authorization_code. Body: {Body}",
                TruncateForLog(responseJson, 500));
            return new DeviceCodePollResult("error");
        }

        var authorizationCode = codeProp.GetString()!;
        var codeVerifier = root.TryGetProperty("code_verifier", out var cvProp)
            ? cvProp.GetString() ?? ""
            : "";
        var codeChallenge = root.TryGetProperty("code_challenge", out var ccProp)
            ? ccProp.GetString() ?? ""
            : "";

        _logger.LogInformation(
            "Got authorization_code (len={CodeLen}), code_verifier (len={VerifierLen}), code_challenge (len={ChallengeLen}). " +
            "redirect_uri={RedirectUri}. Exchanging for tokens...",
            authorizationCode.Length, codeVerifier.Length, codeChallenge.Length,
            $"{_options.Issuer}/deviceauth/callback");

        // Step 4: Exchange the authorization code for tokens (standard PKCE)
        // Device code flow uses the issuer's own callback, NOT the local server.
        // This matches the Codex CLI: redirect_uri = "{issuer}/deviceauth/callback"
        try
        {
            var redirectUri = $"{_options.Issuer}/deviceauth/callback";
            var tokenResponse = await ExchangeCodeForTokensAsync(
                authorizationCode, codeVerifier, redirectUri, ct);

            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogWarning("Device code token exchange returned no access token.");
                return new DeviceCodePollResult("error");
            }

            var (email, accountId) = ExtractIdTokenClaims(tokenResponse.IdToken);
            await StoreEncryptedTokensAsync(userId, tokenResponse, accountId, email, ct);
            var result = await UpsertCodexProviderAsync(userId, email, ct);

            _logger.LogInformation(
                "Device code flow completed for user {UserId}, email={Email}.",
                userId, email);

            return new DeviceCodePollResult("connected", result.Email, result.ProviderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device code token exchange failed.");
            return new DeviceCodePollResult("error");
        }
    }

    private static bool IsDevicePending(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // OpenAI may return various pending indicators
            return true; // Non-success is pending until proven otherwise
        }
        catch
        {
            return true;
        }
    }

    private static bool IsDeviceExpired(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errProp))
            {
                var err = errProp.GetString() ?? "";
                return err.Contains("expired", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Parse a JSON value that may be either a number or a string-encoded number.</summary>
    private static int ParseIntOrString(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetInt32();
        if (elem.ValueKind == JsonValueKind.String &&
            int.TryParse(elem.GetString(), out var val))
            return val;
        return 5;
    }

    private static string TruncateForLog(string s, int maxLen = 200)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";

    // ================================================================
    //  Shared helpers
    // ================================================================

    private async Task<TokenResponse> ExchangeCodeForTokensAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("CodexOAuth");
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri
        };

        var response = await client.PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(payload), ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Codex token exchange failed: {Status} {Body}", response.StatusCode, json);
            throw new InvalidOperationException("Codex token exchange failed.");
        }

        return JsonSerializer.Deserialize<TokenResponse>(json) ?? new TokenResponse();
    }

    private async Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("CodexOAuth");
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["refresh_token"] = refreshToken
        };

        try
        {
            var response = await client.PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(payload), ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Codex token refresh failed: {Status}", response.StatusCode);
                return new TokenResponse();
            }

            return JsonSerializer.Deserialize<TokenResponse>(json) ?? new TokenResponse();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex token refresh encountered an error.");
            return new TokenResponse();
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Best-effort parsing of JWT ID token claims (email, account_id).
    /// Does NOT validate signature -- tokens are received directly from the token endpoint over HTTPS.
    /// </summary>
    private static (string? Email, string? AccountId) ParseIdTokenClaims(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
                return (null, null);

            var payload = parts[1];
            // Fix base64url padding
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);

            string? email = null;
            string? accountId = null;

            if (doc.RootElement.TryGetProperty("email", out var emailProp))
                email = emailProp.GetString();

            if (doc.RootElement.TryGetProperty("account_id", out var accountProp))
                accountId = accountProp.GetString();

            return (email, accountId);
        }
        catch
        {
            return (null, null);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
