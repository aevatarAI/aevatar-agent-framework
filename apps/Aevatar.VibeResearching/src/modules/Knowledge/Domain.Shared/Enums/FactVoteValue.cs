namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Vote value for a fact proposal in the consensus process.
/// Maps to FactVoteValue in proto contracts.
/// </summary>
public enum FactVoteValue
{
    /// <summary>Unspecified vote.</summary>
    Unspecified = 0,

    /// <summary>Approve the fact for promotion to DAG.</summary>
    Approve = 1,

    /// <summary>Reject the fact.</summary>
    Reject = 2,

    /// <summary>Fact needs more work before promotion.</summary>
    NeedsWork = 3
}
