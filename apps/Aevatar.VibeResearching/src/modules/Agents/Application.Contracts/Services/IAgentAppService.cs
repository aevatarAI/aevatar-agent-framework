using Aevatar.VibeResearching.Agents.Application.Contracts.DTOs;
using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Contracts.Services;

/// <summary>
/// Application service for agent provider mapping management.
/// </summary>
public interface IAgentAppService : IApplicationService
{
    /// <summary>
    /// Gets the agent-to-provider mapping for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent provider mapping snapshot</returns>
    Task<object> GetProvidersAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Maps an agent to a specific LLM provider.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="input">Agent provider mapping</param>
    /// <param name="ct">Cancellation token</param>
    Task UpsertProviderAsync(string sessionId, AgentProviderDto input, CancellationToken ct = default);

    /// <summary>
    /// Clears the provider mapping for an agent (reverts to default).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="agentName">Agent name</param>
    /// <param name="ct">Cancellation token</param>
    Task ClearProviderAsync(string sessionId, string agentName, CancellationToken ct = default);
}
