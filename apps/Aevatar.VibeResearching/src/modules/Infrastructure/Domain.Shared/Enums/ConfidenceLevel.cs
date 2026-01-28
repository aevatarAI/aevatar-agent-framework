namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Confidence level for research conclusions.
/// Maps to SraConfidenceLevel in proto contracts.
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>Unspecified confidence level.</summary>
    Unspecified = 0,

    /// <summary>Low confidence in the conclusion.</summary>
    Low = 1,

    /// <summary>Medium confidence in the conclusion.</summary>
    Medium = 2,

    /// <summary>High confidence in the conclusion.</summary>
    High = 3
}
