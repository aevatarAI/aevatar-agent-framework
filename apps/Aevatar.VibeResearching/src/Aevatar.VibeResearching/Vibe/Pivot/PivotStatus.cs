namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Status of a pivot operation.
/// </summary>
public enum PivotStatus
{
    /// <summary>Analyzing user message for direction change intent.</summary>
    Detecting = 0,

    /// <summary>Low confidence detection, waiting for user confirmation.</summary>
    AwaitingConfirmation = 1,

    /// <summary>Soft-deleting pending nodes and creating new plan nodes.</summary>
    UpdatingDAG = 2,

    /// <summary>Broadcasting pivot event to subagents.</summary>
    NotifyingAgents = 3,

    /// <summary>Waiting for subagent acknowledgments.</summary>
    AwaitingAcks = 4,

    /// <summary>Pivot operation completed successfully.</summary>
    Completed = 5,

    /// <summary>Pivot operation failed with error.</summary>
    Failed = 6,

    /// <summary>User requested rollback after pivot completion.</summary>
    RolledBack = 7,

    /// <summary>User declined the pivot during confirmation.</summary>
    Cancelled = 8
}
