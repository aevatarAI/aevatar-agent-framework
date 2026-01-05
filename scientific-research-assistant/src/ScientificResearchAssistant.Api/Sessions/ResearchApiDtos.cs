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

internal sealed class SaveMaterialInDto
{
    public string? Title { get; init; }
    public string? Content { get; init; }
    public string? RelativePath { get; init; }
}


