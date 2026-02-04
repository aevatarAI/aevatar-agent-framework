namespace Aevatar.Agents.Cognitive.Researching.Sessions;

// ============================================================
//  ResearchingInput (Run Input)
//
//  Purpose:
//  - Minimal, framework-level input contract for a researching run.
// ============================================================
public sealed class ResearchingInput
{
    public string? Message { get; init; }
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional per-request provider override (model/runtime selection).
    /// When empty, the session's ProviderName (or LLM default) is used.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Run mode hint (kept for compatibility).
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Optional routing hint:
    /// - null/empty: let research_assistant decide
    /// - ["*"]: broadcast to all background agents
    /// - ["reasoner","librarian"]: target subset
    /// </summary>
    public List<string>? ToAgents { get; init; }

    /// <summary>
    /// Optional attachment references (relative paths under session workspace).
    /// </summary>
    public List<string>? AttachmentPaths { get; init; }
}
