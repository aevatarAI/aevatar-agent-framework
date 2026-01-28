namespace Aevatar.VibeResearching.Sessions.Enums;

/// <summary>
/// Risk level for compute operations.
/// Maps to SraComputeRiskLevel in proto contracts.
/// </summary>
public enum ComputeRiskLevel
{
    /// <summary>Unspecified risk level.</summary>
    Unspecified = 0,

    /// <summary>Low risk operation.</summary>
    Low = 1,

    /// <summary>Medium risk operation.</summary>
    Medium = 2,

    /// <summary>High risk operation.</summary>
    High = 3
}
