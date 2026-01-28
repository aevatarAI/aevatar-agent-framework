using Aevatar.VibeResearching.Agents.Application.Contracts.DTOs;
using Aevatar.VibeResearching.Agents.Application.Contracts.Services;
using Aevatar.VibeResearching.Sessions.Repositories;
using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Services;

/// <summary>
/// Application service for agent provider mapping management.
/// Delegates to agent providers repository.
/// </summary>
public class AgentAppService : ApplicationService, IAgentAppService
{
    private readonly IAgentProvidersRepository _agentProvidersRepository;

    public AgentAppService(IAgentProvidersRepository agentProvidersRepository)
    {
        _agentProvidersRepository = agentProvidersRepository;
    }

    /// <inheritdoc/>
    public async Task<object> GetProvidersAsync(string sessionId, CancellationToken ct = default)
    {
        return await _agentProvidersRepository.LoadAsync(sessionId, ct)
            ?? (object)new { agents = Array.Empty<object>() };
    }

    /// <inheritdoc/>
    public async Task UpsertProviderAsync(string sessionId, AgentProviderDto input, CancellationToken ct = default)
    {
        await _agentProvidersRepository.UpsertAsync(
            sessionId,
            input.AgentName ?? string.Empty,
            input.ProviderName ?? string.Empty,
            ct);
    }

    /// <inheritdoc/>
    public async Task ClearProviderAsync(string sessionId, string agentName, CancellationToken ct = default)
    {
        await _agentProvidersRepository.UpsertAsync(sessionId, agentName, string.Empty, ct);
    }
}
