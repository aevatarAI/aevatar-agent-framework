using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Abstractions;

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
    /// Tool name (optional, populated by caller when available).
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Tool call id (optional, used for progress correlation).
    /// </summary>
    public string? ToolCallId { get; set; }
    
    /// <summary>
    /// Additional context data
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    // ============================================================
    //  UI / progress (best-effort)
    //
    //  WHY:
    //  - Some tools are long-running (e.g., embeddings index build).
    //  - Without intermediate signals, the UI looks "stuck" even though work continues.
    //  - This callback allows the caller (host app) to project progress to UI (AG-UI).
    //
    //  NOTE:
    //  - Tools MUST treat this as optional and best-effort.
    //  - Tools should keep payload small and low-frequency (caller may throttle).
    // ============================================================
    public Func<string, CancellationToken, Task>? ReportProgressAsync { get; set; }

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