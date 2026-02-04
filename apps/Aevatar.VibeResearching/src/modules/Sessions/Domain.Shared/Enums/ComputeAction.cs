namespace Aevatar.VibeResearching.Sessions.Enums;

/// <summary>
/// Recommended action for a compute request.
/// Maps to SraComputeAction in proto contracts.
/// </summary>
public enum ComputeAction
{
    /// <summary>Unspecified action.</summary>
    Unspecified = 0,

    /// <summary>Execute the compute operation.</summary>
    Execute = 1,

    /// <summary>Degrade to a simpler/cheaper alternative.</summary>
    Degrade = 2,

    /// <summary>Skip the compute operation.</summary>
    Skip = 3
}
