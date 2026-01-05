using Aevatar.Agents.AI.WithTool.Abstractions;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI processing strategy dependencies
/// Contains external dependencies required for strategy execution
/// </summary>
public class AevatarAIStrategyDependencies
{
    /// <summary>
    /// LLM provider
    /// </summary>
    public IAevatarLLMProvider LLMProvider { get; init; } = null!;
    
    /// <summary>
    /// Prompt manager
    /// </summary>
    public IAevatarPromptManager PromptManager { get; init; } = null!;
    
    /// <summary>
    /// Tool manager
    /// </summary>
    public IAevatarToolManager ToolManager { get; init; } = null!;
    
    /// <summary>
    /// AI configuration
    /// </summary>
    public AevatarAIAgentConfiguration Configuration { get; init; } = null!;
    
    /// <summary>
    /// Logger
    /// </summary>
    public Microsoft.Extensions.Logging.ILogger? Logger { get; init; }
    
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; init; } = string.Empty;
    
    /// <summary>
    /// Event publish callback
    /// </summary>
    public Func<Google.Protobuf.IMessage, Task>? PublishEventCallback { get; init; }
    
    /// <summary>
    /// Tool execution callback
    /// </summary>
    public Func<string, Dictionary<string, object>, CancellationToken, Task<object?>>? ExecuteToolCallback { get; init; }
}