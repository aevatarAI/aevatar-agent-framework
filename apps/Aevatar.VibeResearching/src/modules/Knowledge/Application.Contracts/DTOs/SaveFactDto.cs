namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Input DTO for creating a fact proposal.
/// </summary>
public sealed class SaveFactDto
{
    /// <summary>
    /// Optional short title for the fact.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Fact content (Markdown supported, required).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Optional relative path for file-backed storage.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// Optional evidence references (workspace artifacts or external refs).
    /// </summary>
    public List<string>? EvidencePaths { get; init; }

    /// <summary>
    /// Who proposed this fact (agent name, user ID, or "api").
    /// </summary>
    public string? ProposedBy { get; init; }
}
