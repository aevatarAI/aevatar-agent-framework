namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor factory provider interface
/// Used to provide factory methods for Agent types
/// </summary>
public interface IGAgentActorFactoryProvider
{
    /// <summary>
    /// Register factory method for specified Agent type
    /// </summary>
    /// <typeparam name="TAgent">Agent type</typeparam>
    /// <param name="factory">Factory delegate</param>
    void RegisterFactory<TAgent>(Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
        where TAgent : IGAgent;
    
    /// <summary>
    /// Register factory method for specified Agent type (using type parameter)
    /// </summary>
    /// <param name="agentType">Agent type</param>
    /// <param name="factory">Factory delegate</param>
    void RegisterFactory(Type agentType, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory);
    
    /// <summary>
    /// Get factory method for specified Agent type
    /// </summary>
    /// <param name="agentType">Agent type</param>
    /// <returns>Factory delegate, returns null if not found</returns>
    Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType);
}