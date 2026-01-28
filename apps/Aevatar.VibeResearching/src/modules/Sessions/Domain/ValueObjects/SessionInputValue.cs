namespace Aevatar.VibeResearching.Sessions.ValueObjects;

/// <summary>
/// Session input value object.
/// Represents user input to a research session.
/// </summary>
public sealed class SessionInputValue
{
    public string? Message { get; init; }
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional per-request provider override (model/runtime selection).
    /// When empty, the session's ProviderName (or LLM default) is used.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Run mode:
    /// - "chat" (default): MCP/tools powered chat
    /// - "vibe": multi-agent axioms+references reasoning
    /// - "vibe_loop": repeat vibe rounds until goal verifier passes (bounded by loop budgets)
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Optional loop configuration for mode=vibe_loop.
    /// </summary>
    public VibeLoopValue? Loop { get; init; }

    /// <summary>
    /// Optional routing hint for vibe researching:
    /// - null/empty: let research_assistant decide
    /// - ["*"]: broadcast to all background agents
    /// - ["reasoner","librarian"]: target subset
    /// </summary>
    public List<string>? ToAgents { get; init; }

    /// <summary>
    /// Optional attachment references (relative paths under session workspace),
    /// typically returned by POST /api/sessions/{id}/uploads.
    /// </summary>
    public List<string>? AttachmentPaths { get; init; }
}

public sealed class VibeLoopValue
{
    /// <summary>
    /// Hard cap for loop rounds (safety valve).
    /// </summary>
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Total wall-clock budget for the whole loop (ms).
    /// </summary>
    public int? MaxTotalDurationMs { get; init; }
}
