namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Input DTO for recording a compute execution decision.
/// </summary>
public sealed class ComputeDecisionDto
{
    /// <summary>
    /// Compute plan identifier.
    /// </summary>
    public string? PlanId { get; init; }

    /// <summary>
    /// Decision action: "execute", "degrade", or "skip".
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Decision action (alias for Action property).
    /// </summary>
    public string? Decision => Action;

    /// <summary>
    /// Optional comment explaining the decision.
    /// </summary>
    public string? Comment { get; init; }
}
