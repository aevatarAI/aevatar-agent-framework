namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM response
/// </summary>
public class AevatarLLMResponse
{
    /// <summary>
    /// Generated content
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Function call (if any)
    /// </summary>
    public AevatarFunctionCall? AevatarFunctionCall { get; set; }
    
    /// <summary>
    /// Stop reason
    /// </summary>
    public AevatarStopReason AevatarStopReason { get; set; }
    
    /// <summary>
    /// Token usage
    /// </summary>
    public AevatarTokenUsage? Usage { get; set; }
    
    /// <summary>
    /// Model name
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// Metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}