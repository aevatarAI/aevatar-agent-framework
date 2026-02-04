namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Final decision for a fact proposal.
/// Maps to FactDecisionValue in proto contracts.
/// </summary>
public enum FactDecisionValue
{
    /// <summary>Unspecified decision.</summary>
    Unspecified = 0,

    /// <summary>Promote the fact to the knowledge DAG.</summary>
    Promote = 1,

    /// <summary>Reject the fact proposal.</summary>
    Reject = 2
}
