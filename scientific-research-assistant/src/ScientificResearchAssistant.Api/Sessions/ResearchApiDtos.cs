namespace ScientificResearchAssistant.Api.Sessions;

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
    /// Run mode:
    /// - "chat" (default): MCP/tools powered chat
    /// - "vibe": multi-agent axioms+references reasoning
    /// </summary>
    public string? Mode { get; init; }
}

internal sealed class SaveFactInDto
{
    public string? Title { get; init; }
    public string? Content { get; init; }
    public string? RelativePath { get; init; }

    // Optional evidence references (sources/* or workspace artifacts/*).
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


