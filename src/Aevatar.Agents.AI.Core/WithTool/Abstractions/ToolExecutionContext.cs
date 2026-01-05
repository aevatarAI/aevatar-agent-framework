using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// Tool execution context
/// </summary>
public class ToolExecutionContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Tool manager
    /// </summary>
    public IAevatarToolManager ToolManager { get; set; } = null!;
    
    /// <summary>
    /// Event publish callback
    /// </summary>
    public Func<IMessage, Task>? PublishEventCallback { get; set; }

    /// <summary>
    /// Event publish callback with explicit propagation direction.
    /// </summary>
    public Func<IMessage, EventDirection, CancellationToken, Task<string>>? PublishEventWithDirectionCallback { get; set; }
    
    /// <summary>
    /// Function to get session ID
    /// </summary>
    public Func<string> GetSessionId { get; set; } = () => Guid.NewGuid().ToString();
    
    /// <summary>
    /// Logger
    /// </summary>
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// Additional context data
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    // ============================================================
    //  Safety policy (best-effort, caller-controlled)
    // ============================================================

    /// <summary>
    /// Whether tools marked <c>RequiresInternalAccess</c> are allowed to execute.
    /// Default: false (caller must opt-in explicitly).
    /// </summary>
    public bool AllowInternalTools { get; set; }

    /// <summary>
    /// Whether tools marked <c>IsDangerous</c> or <c>RequiresConfirmation</c> are allowed to execute.
    /// Default: false (caller must opt-in explicitly).
    /// </summary>
    public bool AllowDangerousTools { get; set; }
}