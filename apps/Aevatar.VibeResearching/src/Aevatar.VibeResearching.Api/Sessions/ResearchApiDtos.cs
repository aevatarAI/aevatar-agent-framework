namespace VibeResearching.Api.Sessions;

// ============================================================
//  DTOs (HTTP API)
//
//  Keep these separate from endpoint mapping so we can reuse them
//  across the run executor, session API, etc.
// ============================================================

internal sealed record CreateSessionInDto(string? ProviderName);

internal sealed class SessionInputInDto
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
    /// </summary>
    public string? Mode { get; init; }

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

internal sealed class ComputeDecisionInDto
{
    public string? PlanId { get; init; }
    public string? Action { get; init; } // execute | degrade | skip
    public string? Comment { get; init; }
}

internal sealed class SaveFactInDto
{
    public string? Title { get; init; }
    public string? Content { get; init; }
    public string? RelativePath { get; init; }

    // Optional evidence references (workspace artifacts or external refs).
    public List<string>? EvidencePaths { get; init; }
}

internal sealed class FactVoteInDto
{
    public string? ReviewerId { get; init; }
    public string? Vote { get; init; } // approve | reject | needs_work
    public string? Comment { get; init; }
}

internal sealed class FactVerificationInDto
{
    public string? VerifierId { get; init; }
    public string? Tool { get; init; } // e.g., "python_exec"
    public bool? Result { get; init; }
    public List<string>? ArtifactPaths { get; init; }
    public string? LogExcerpt { get; init; }
}

internal sealed class PromoteFactInDto
{
    public string? FinalizedBy { get; init; }
}

internal sealed class SaveFileInDto
{
    public string? Path { get; init; }
    public string? Content { get; init; }
}


