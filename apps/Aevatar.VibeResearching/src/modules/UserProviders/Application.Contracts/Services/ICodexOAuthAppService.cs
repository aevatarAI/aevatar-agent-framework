using Aevatar.VibeResearching.UserProviders.DTOs;

namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Application service for Codex OAuth flow management.
/// </summary>
public interface ICodexOAuthAppService
{
    Task<CodexInitiateResultDto> InitiateAsync(CodexInitiateRequestDto input, CancellationToken ct = default);
    Task<CodexCallbackResultDto> HandleCallbackAsync(CodexCallbackDto input, CancellationToken ct = default);
    Task<CodexStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Returns the configured auth mode ("localhost" or "devicecode").</summary>
    string GetAuthMode();

    // --- Device Code Flow ---
    Task<DeviceCodeInitiateResultDto> InitiateDeviceCodeAsync(CancellationToken ct = default);
    Task<DeviceCodePollResultDto> PollDeviceCodeAsync(DeviceCodePollRequestDto input, CancellationToken ct = default);
}
