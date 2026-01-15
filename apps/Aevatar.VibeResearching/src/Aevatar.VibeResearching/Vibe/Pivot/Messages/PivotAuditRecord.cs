using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Vibe.Pivot.Messages;

/// <summary>
/// Immutable audit record for a pivot operation.
/// Used for session history and audit trail.
/// </summary>
public sealed record PivotAuditRecord
{
    /// <summary>Unique record ID.</summary>
    public required string RecordId { get; init; }

    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Pivot operation ID.</summary>
    public required string PivotId { get; init; }

    /// <summary>Trigger message ID.</summary>
    public required string TriggerMessageId { get; init; }

    /// <summary>Truncated trigger message text (max 500 chars).</summary>
    public required string TriggerMessageText { get; init; }

    /// <summary>Previous research direction.</summary>
    public required string OldDirection { get; init; }

    /// <summary>New research direction.</summary>
    public required string NewDirection { get; init; }

    /// <summary>Detection confidence score.</summary>
    public double Confidence { get; init; }

    /// <summary>Number of nodes cancelled.</summary>
    public int CancelledNodeCount { get; init; }

    /// <summary>Number of nodes preserved.</summary>
    public int PreservedNodeCount { get; init; }

    /// <summary>Number of new nodes created.</summary>
    public int NewNodeCount { get; init; }

    /// <summary>Whether user explicitly confirmed the pivot.</summary>
    public bool UserConfirmed { get; init; }

    /// <summary>Total operation duration in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Final status: "completed", "failed", "rolled_back".</summary>
    public required string Status { get; init; }

    /// <summary>Record creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an audit record from a completed pivot operation.
    /// </summary>
    public static PivotAuditRecord FromPivotOperation(PivotOperation operation, bool userConfirmed)
    {
        var triggerText = operation.Intent.NewTopic ?? "";
        if (triggerText.Length > 500)
            triggerText = triggerText[..500];

        var status = operation.Status switch
        {
            PivotStatus.Completed => "completed",
            PivotStatus.Failed => "failed",
            PivotStatus.RolledBack => "rolled_back",
            PivotStatus.Cancelled => "cancelled",
            _ => "unknown"
        };

        return new PivotAuditRecord
        {
            RecordId = Guid.NewGuid().ToString(),
            SessionId = operation.SessionId,
            PivotId = operation.PivotId,
            TriggerMessageId = operation.Intent.MessageId,
            TriggerMessageText = triggerText,
            OldDirection = operation.OldDirection ?? "",
            NewDirection = operation.NewDirection ?? operation.Intent.NewTopic ?? "",
            Confidence = operation.Intent.Confidence,
            CancelledNodeCount = operation.CancelledNodeIds.Count,
            PreservedNodeCount = operation.PreservedNodeIds.Count,
            NewNodeCount = operation.NewNodeIds.Count,
            UserConfirmed = userConfirmed,
            DurationMs = operation.DurationMs,
            Status = status
        };
    }
}
