namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Status of a knowledge node with respect to research direction pivots.
/// </summary>
public enum PivotNodeStatus
{
    /// <summary>
    /// Node is part of active research (default state).
    /// </summary>
    Active = 0,

    /// <summary>
    /// Node was cancelled due to a pivot operation (soft-deleted).
    /// The node is retained in the DAG for history/audit purposes.
    /// </summary>
    Cancelled = 1,

    /// <summary>
    /// Completed node marked as belonging to a previous research direction.
    /// Used to preserve valuable completed work while indicating it's from a superseded direction.
    /// </summary>
    Superseded = 2
}
