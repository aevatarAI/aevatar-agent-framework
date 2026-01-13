namespace ScientificResearchAssistant.Vibe.Pivot.Models;

/// <summary>
/// Tracks the state of an in-progress pivot operation.
/// </summary>
public sealed class PivotOperation
{
    /// <summary>Unique pivot operation ID (GUID).</summary>
    public required string PivotId { get; init; }

    /// <summary>Session being pivoted.</summary>
    public required string SessionId { get; init; }

    /// <summary>The triggering intent.</summary>
    public required DirectionChangeIntent Intent { get; init; }

    /// <summary>Current operation status.</summary>
    public PivotStatus Status { get; set; } = PivotStatus.Detecting;

    /// <summary>DAG snapshot ID for rollback (set before modifications).</summary>
    public string? SnapshotId { get; set; }

    /// <summary>IDs of nodes being cancelled.</summary>
    public List<string> CancelledNodeIds { get; init; } = [];

    /// <summary>IDs of nodes being preserved.</summary>
    public List<string> PreservedNodeIds { get; init; } = [];

    /// <summary>IDs of new nodes created for the new direction.</summary>
    public List<string> NewNodeIds { get; init; } = [];

    /// <summary>Acknowledgment tracking: agent ID -> acknowledged.</summary>
    public Dictionary<string, bool> SubagentAcks { get; init; } = [];

    /// <summary>Operation start time.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Operation completion time (null if still in progress).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Error details if status is Failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Previous research direction summary.</summary>
    public string? OldDirection { get; set; }

    /// <summary>New research direction.</summary>
    public string? NewDirection { get; set; }

    /// <summary>
    /// Returns the total duration of the pivot operation in milliseconds.
    /// Returns -1 if not yet completed.
    /// </summary>
    public long DurationMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : -1;

    /// <summary>
    /// Marks the operation as completed with the given status.
    /// </summary>
    public void Complete(PivotStatus finalStatus, string? errorMessage = null)
    {
        Status = finalStatus;
        CompletedAt = DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a new pivot operation for a detected intent.
    /// </summary>
    public static PivotOperation Create(DirectionChangeIntent intent)
    {
        return new PivotOperation
        {
            PivotId = Guid.NewGuid().ToString(),
            SessionId = intent.SessionId,
            Intent = intent
        };
    }
}
