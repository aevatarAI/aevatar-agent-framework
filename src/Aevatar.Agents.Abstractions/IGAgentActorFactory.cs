namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor factory interface
/// Used to create Actor instances for different runtimes
/// </summary>
public interface IGAgentActorFactory
{
    /// <summary>
    /// Create Agent Actor (automatically infers state type)
    /// </summary>
    /// <param name="id">Agent ID (format varies by runtime)</param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="TAgent">Agent type</typeparam>
    /// <returns>Actor instance</returns>
    Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(string? id = null, CancellationToken ct = default)
        where TAgent : IGAgent;
}