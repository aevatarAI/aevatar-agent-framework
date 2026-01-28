using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.Pivot.Messages;

/// <summary>
/// Request to rollback a previous pivot operation.
/// </summary>
public sealed record RollbackRequest
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Pivot ID to rollback (empty = most recent).</summary>
    public string? PivotId { get; init; }

    /// <summary>Whether to preserve completed work from the new direction.</summary>
    public bool PreserveNewCompleted { get; init; }

    /// <summary>Request timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
