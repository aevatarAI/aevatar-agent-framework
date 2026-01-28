using Aevatar.VibeResearching.Sessions.ValueObjects;

namespace Aevatar.VibeResearching.Sessions.Repositories;

/// <summary>
/// Repository interface for agent providers configuration.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IAgentProvidersRepository
{
    /// <summary>
    /// Loads agent provider configuration from storage.
    /// </summary>
    Task<AgentProvidersSnapshot?> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Upserts an agent provider mapping.
    /// </summary>
    Task UpsertAsync(string sessionId, string agentName, string providerName, CancellationToken ct);
}
