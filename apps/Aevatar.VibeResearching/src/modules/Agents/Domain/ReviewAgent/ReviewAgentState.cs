using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Status values for the Review Agent.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewAgentStatus
{
    /// <summary>Waiting for next scheduled round.</summary>
    Idle,

    /// <summary>Processing knowledge nodes for verification.</summary>
    WorkingReviewRound,

    /// <summary>Removing expired deactivated nodes.</summary>
    WorkingCleanupRound,

    /// <summary>Failed state (will retry next interval).</summary>
    Error
}

/// <summary>
/// Runtime state of the Review Agent service.
/// Persisted to workspace/review-agent/state.json for restart recovery.
/// </summary>
public sealed class ReviewAgentState
{
    /// <summary>Current agent status.</summary>
    public ReviewAgentStatus Status { get; set; } = ReviewAgentStatus.Idle;

    /// <summary>ID of the active iteration (null if idle).</summary>
    public string? CurrentIterationId { get; set; }

    /// <summary>Timestamp of last completed round.</summary>
    public DateTimeOffset? LastCompletedAt { get; set; }

    /// <summary>Timestamp of next scheduled round.</summary>
    public DateTimeOffset? NextScheduledAt { get; set; }

    /// <summary>Counter for nodes reviewed in current round.</summary>
    public int NodesReviewed { get; set; }

    /// <summary>Counter for nodes pending review in current round.</summary>
    public int NodesPending { get; set; }

    /// <summary>Counter for nodes deactivated in current round.</summary>
    public int NodesDeactivated { get; set; }

    /// <summary>Counter for nodes removed in current cleanup round.</summary>
    public int NodesRemoved { get; set; }

    /// <summary>Error message if status is Error.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Review log entries for the current iteration (cleared on iteration complete).</summary>
    public List<ReviewLogEntry> CurrentIterationEntries { get; set; } = [];

    /// <summary>
    /// Reset counters for a new round.
    /// </summary>
    public void ResetCounters()
    {
        NodesReviewed = 0;
        NodesPending = 0;
        NodesDeactivated = 0;
        NodesRemoved = 0;
        ErrorMessage = null;
        CurrentIterationEntries.Clear();
    }

    /// <summary>
    /// Create a snapshot for API response.
    /// </summary>
    public ReviewAgentState Clone() => new()
    {
        Status = Status,
        CurrentIterationId = CurrentIterationId,
        LastCompletedAt = LastCompletedAt,
        NextScheduledAt = NextScheduledAt,
        NodesReviewed = NodesReviewed,
        NodesPending = NodesPending,
        NodesDeactivated = NodesDeactivated,
        NodesRemoved = NodesRemoved,
        ErrorMessage = ErrorMessage,
        CurrentIterationEntries = [.. CurrentIterationEntries] // shallow copy
    };
}
