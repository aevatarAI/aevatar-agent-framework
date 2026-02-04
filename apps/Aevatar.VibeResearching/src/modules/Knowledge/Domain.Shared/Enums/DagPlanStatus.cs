namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Execution status for plan nodes in the DAG.
/// Maps to SraDagPlanStatus in proto contracts.
/// </summary>
public enum DagPlanStatus
{
    /// <summary>Unspecified status.</summary>
    Unspecified = 0,

    /// <summary>Plan step is pending execution.</summary>
    Pending = 1,

    /// <summary>Plan step is actively being worked on.</summary>
    Active = 2,

    /// <summary>Plan step has been completed.</summary>
    Completed = 3,

    /// <summary>Plan step was cancelled due to direction change.</summary>
    Cancelled = 4
}
