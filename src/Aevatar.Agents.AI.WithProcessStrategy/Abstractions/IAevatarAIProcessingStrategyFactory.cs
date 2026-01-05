using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;

namespace Aevatar.Agents.AI.WithProcessStrategy.Abstractions;

/// <summary>
/// AI processing strategy factory interface
/// </summary>
public interface IAevatarAIProcessingStrategyFactory
{
    /// <summary>
    /// Get processing strategy
    /// </summary>
    IAevatarAIProcessingStrategy GetStrategy(AevatarAIProcessingMode mode);
    
    /// <summary>
    /// Get or create processing strategy
    /// </summary>
    IAevatarAIProcessingStrategy GetOrCreateStrategy(AevatarAIProcessingMode mode, bool useCache = true);
    
    /// <summary>
    /// Register custom strategy type
    /// </summary>
    void RegisterStrategyType(AevatarAIProcessingMode mode, Type strategyType);
    
    /// <summary>
    /// Register custom strategy instance
    /// </summary>
    void RegisterStrategy(AevatarAIProcessingMode mode, IAevatarAIProcessingStrategy strategy);
    
    /// <summary>
    /// Get all available processing modes
    /// </summary>
    IEnumerable<AevatarAIProcessingMode> GetAvailableModes();
    
    /// <summary>
    /// Clear strategy cache
    /// </summary>
    void ClearCache();
}