using Aevatar.VibeResearching.UserProviders.DTOs;

namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Application service for session-level agent-provider mapping management.
/// </summary>
public interface ISessionProviderAppService
{
    /// <summary>
    /// Gets the current agent-provider mappings for a session.
    /// </summary>
    Task<AgentProvidersResponseDto> GetAgentProvidersAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Updates agent-provider mappings for a session and returns the new state.
    /// </summary>
    Task<AgentProvidersResponseDto> UpdateAgentProvidersAsync(
        string sessionId, UpdateAgentProvidersDto input, CancellationToken ct = default);

    /// <summary>
    /// Gets all available providers (user + platform) for the current user.
    /// </summary>
    Task<IReadOnlyList<AvailableProviderDto>> GetAvailableProvidersAsync(CancellationToken ct = default);
}
