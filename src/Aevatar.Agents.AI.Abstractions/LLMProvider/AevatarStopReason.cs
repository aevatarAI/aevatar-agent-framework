namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM generation stop reason
/// </summary>
public enum AevatarStopReason
{
    /// <summary>
    /// Normal completion (model naturally ended)
    /// </summary>
    Complete,
    
    /// <summary>
    /// Reached maximum token limit
    /// </summary>
    MaxTokens,
    
    /// <summary>
    /// Encountered stop sequence
    /// </summary>
    StopSequence,
    
    /// <summary>
    /// Function/tool call required
    /// </summary>
    AevatarFunctionCall,
    
    /// <summary>
    /// Content blocked by safety filter
    /// </summary>
    ContentFilter,
    
    /// <summary>
    /// User actively interrupted
    /// </summary>
    UserInterruption,
    
    /// <summary>
    /// Request timeout
    /// </summary>
    Timeout,
    
    /// <summary>
    /// Reached API rate limit
    /// </summary>
    RateLimitReached,
    
    /// <summary>
    /// Context length exceeded
    /// </summary>
    ContextLengthExceeded,
    
    /// <summary>
    /// Error occurred
    /// </summary>
    Error
}