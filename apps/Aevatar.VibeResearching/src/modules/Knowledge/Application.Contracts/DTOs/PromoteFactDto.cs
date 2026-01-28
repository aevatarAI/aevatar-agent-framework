namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Input DTO for promoting a fact from proposal to DAG.
/// </summary>
public sealed class PromoteFactDto
{
    /// <summary>
    /// Agent/user identifier finalizing the decision (defaults to "api").
    /// </summary>
    public string? FinalizedBy { get; init; }
}
