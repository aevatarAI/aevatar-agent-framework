using Aevatar.Agents.AI.WithProcessStrategy.Messages;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI processing strategy interface
/// Defines common contract for different AI processing modes
/// </summary>
// ReSharper disable once InconsistentNaming
public interface IAevatarAIProcessingStrategy
{
    /// <summary>
    /// Get strategy name
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Get strategy description
    /// Describes the functionality and purpose of this strategy
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Get processing mode
    /// </summary>
    AevatarAIProcessingMode Mode { get; }
    
    /// <summary>
    /// Determine if this strategy can handle the given AI context
    /// Allows strategy to decide if it's suitable based on context content
    /// </summary>
    /// <param name="context">AI context</param>
    /// <returns>Returns true if strategy can handle the context, otherwise false</returns>
    bool CanHandle(AevatarAIContext context);
    
    /// <summary>
    /// Estimate complexity of processing the given context
    /// </summary>
    /// <param name="context">AI context</param>
    /// <returns>Complexity score from 0 (simple) to 1 (complex)</returns>
    double EstimateComplexity(AevatarAIContext context);
    
    /// <summary>
    /// Validate if strategy has all required dependencies
    /// </summary>
    /// <param name="dependencies">Strategy dependencies</param>
    /// <returns>Returns true if all dependencies are satisfied, otherwise false</returns>
    bool ValidateRequirements(AevatarAIStrategyDependencies dependencies);
    
    /// <summary>
    /// Process AI request
    /// </summary>
    /// <param name="context">AI context</param>
    /// <param name="dependencies">Strategy dependencies</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result</returns>
    Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default);
}