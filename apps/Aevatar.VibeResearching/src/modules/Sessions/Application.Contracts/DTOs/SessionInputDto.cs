namespace Aevatar.VibeResearching.Sessions.DTOs;

/// <summary>
/// Input DTO for submitting user input to a research session.
/// </summary>
public sealed class SessionInputDto
{
    /// <summary>
    /// User message/query text (required).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional request correlation ID for tracing.
    /// </summary>
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
    public VibeLoopDto? Loop { get; init; }

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
