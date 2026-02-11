namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM request
/// </summary>
public class AevatarLLMRequest
{
    /// <summary>
    /// System prompt
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// User prompt
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// Conversation history
    /// </summary>
    public IList<AevatarChatMessage> Messages { get; set; } = new List<AevatarChatMessage>();
    
    /// <summary>
    /// Model settings
    /// </summary>
    public AevatarLLMSettings Settings { get; set; } = new();
    
    /// <summary>
    /// Function/tool definitions (for Function Calling)
    /// </summary>
    public IList<AevatarFunctionDefinition>? Functions { get; set; }
    
    /// <summary>
    /// Additional information in context window
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>
    /// Images for multimodal input (already resolved with data)
    /// </summary>
    public IList<AevatarImageData>? Images { get; set; }
}