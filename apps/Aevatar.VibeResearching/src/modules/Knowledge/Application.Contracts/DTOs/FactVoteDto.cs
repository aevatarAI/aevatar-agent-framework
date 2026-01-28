namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Input DTO for recording a fact review vote.
/// </summary>
public sealed class FactVoteDto
{
    /// <summary>
    /// Reviewer agent/user identifier (required).
    /// </summary>
    public string? ReviewerId { get; init; }

    /// <summary>
    /// Vote value: "approve", "reject", or "needs_work" (required).
    /// </summary>
    public string? Vote { get; init; }

    /// <summary>
    /// Optional comment explaining the vote.
    /// </summary>
    public string? Comment { get; init; }
}
