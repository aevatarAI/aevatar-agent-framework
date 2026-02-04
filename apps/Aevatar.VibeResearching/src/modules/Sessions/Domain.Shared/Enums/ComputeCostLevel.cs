namespace Aevatar.VibeResearching.Sessions.Enums;

/// <summary>
/// Cost level for compute operations.
/// Maps to SraComputeCostLevel in proto contracts.
/// </summary>
public enum ComputeCostLevel
{
    /// <summary>Unspecified cost level.</summary>
    Unspecified = 0,

    /// <summary>Low cost operation.</summary>
    Low = 1,

    /// <summary>Medium cost operation.</summary>
    Medium = 2,

    /// <summary>High cost operation.</summary>
    High = 3
}
