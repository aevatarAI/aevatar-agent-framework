using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Services;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.UserProviders.Application.Services;

/// <summary>
/// Application service that orchestrates the Codex OAuth flow.
/// Delegates to ICodexOAuthService for the actual OAuth operations.
/// </summary>
public sealed class CodexOAuthAppService : ICodexOAuthAppService
{
    private readonly ICodexOAuthService _codexService;
    private readonly ICurrentUser _currentUser;

    public CodexOAuthAppService(
        ICodexOAuthService codexService,
        ICurrentUser currentUser)
    {
        _codexService = codexService;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<CodexInitiateResultDto> InitiateAsync(
        CodexInitiateRequestDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(input.RedirectUri))
            throw new ArgumentException("Redirect URI is required.");

        var result = await _codexService.InitiateAsync(userId, input.RedirectUri, ct);

        return new CodexInitiateResultDto
        {
            AuthUrl = result.AuthUrl,
            State = result.State
        };
    }

    /// <inheritdoc />
    public async Task<CodexCallbackResultDto> HandleCallbackAsync(
        CodexCallbackDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(input.Code))
            throw new ArgumentException("Authorization code is required.");

        if (string.IsNullOrWhiteSpace(input.State))
            throw new ArgumentException("State parameter is required.");

        var result = await _codexService.HandleCallbackAsync(
            userId, input.Code, input.State, input.RedirectUri, ct);

        return new CodexCallbackResultDto
        {
            Status = result.Status,
            Email = result.Email,
            ProviderId = result.ProviderId
        };
    }

    /// <inheritdoc />
    public async Task<CodexStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var info = await _codexService.GetStatusAsync(userId, ct);

        return new CodexStatusDto
        {
            Connected = info.Connected,
            Email = info.Email,
            ConnectedAt = info.ConnectedAt,
            ProviderId = info.ProviderId
        };
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        await _codexService.DisconnectAsync(userId, ct);
    }

    /// <inheritdoc />
    public string GetAuthMode() => _codexService.GetAuthMode();

    /// <inheritdoc />
    public async Task<DeviceCodeInitiateResultDto> InitiateDeviceCodeAsync(CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await _codexService.InitiateDeviceCodeAsync(userId, ct);

        return new DeviceCodeInitiateResultDto
        {
            DeviceAuthId = result.DeviceAuthId,
            UserCode = result.UserCode,
            VerificationUri = result.VerificationUri,
            Interval = result.Interval
        };
    }

    /// <inheritdoc />
    public async Task<DeviceCodePollResultDto> PollDeviceCodeAsync(
        DeviceCodePollRequestDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(input.DeviceAuthId))
            throw new ArgumentException("Device auth ID is required.");

        if (string.IsNullOrWhiteSpace(input.UserCode))
            throw new ArgumentException("User code is required.");

        var result = await _codexService.PollDeviceCodeAsync(
            userId, input.DeviceAuthId, input.UserCode, ct);

        return new DeviceCodePollResultDto
        {
            Status = result.Status,
            Email = result.Email,
            ProviderId = result.ProviderId
        };
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.Id
            ?? throw new UnauthorizedAccessException("User is not authenticated.");
    }
}
