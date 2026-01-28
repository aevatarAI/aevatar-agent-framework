namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Constants for pivot detection and direction change handling.
/// </summary>
public static class PivotConsts
{
    /// <summary>
    /// Maximum number of pivot history entries to keep.
    /// </summary>
    public const int MaxPivotHistory = 100;

    /// <summary>
    /// Confidence threshold for detecting direction change (0.0 to 1.0).
    /// </summary>
    public const double DirectionChangeConfidenceThreshold = 0.7;

    /// <summary>
    /// Maximum number of cancelled plan nodes per pivot.
    /// </summary>
    public const int MaxCancelledNodesPerPivot = 500;

    /// <summary>
    /// Maximum number of new milestones to create during pivot.
    /// </summary>
    public const int MaxNewMilestonesPerPivot = 20;
}
