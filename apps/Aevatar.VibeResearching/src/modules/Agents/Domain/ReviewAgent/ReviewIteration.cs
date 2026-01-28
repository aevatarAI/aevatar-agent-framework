using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Result of reviewing a knowledge node.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewResult
{
    /// <summary>Node verified successfully.</summary>
    Passed,

    /// <summary>Node failed verification (deactivated).</summary>
    Failed,

    /// <summary>Node was already deactivated (skipped).</summary>
    Skipped
}

/// <summary>
/// Record of individual knowledge node review.
/// Embedded within ReviewIteration.Entries.
/// </summary>
public sealed class ReviewLogEntry
{
    /// <summary>Unique entry ID (format: {iterationId}_{nodeId}).</summary>
    public required string EntryId { get; init; }

    /// <summary>Knowledge node ID being reviewed.</summary>
    public required string NodeId { get; init; }

    /// <summary>Node label/title at time of review.</summary>
    public required string NodeLabel { get; init; }

    /// <summary>Explanation content (detailed description, proof, derivation, references).</summary>
    public string? ExplainContent { get; init; }

    /// <summary>List of dependency node IDs.</summary>
    public List<string> Dependencies { get; init; } = [];

    /// <summary>Review result.</summary>
    public ReviewResult ReviewResult { get; set; } = ReviewResult.Skipped;

    /// <summary>Reason for failure (null if passed).</summary>
    public string? DeactivatedReason { get; set; }

    /// <summary>When this node was reviewed.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Full verification reasoning (for audit).</summary>
    public string? VerificationContent { get; set; }
}

/// <summary>
/// Record of a complete review cycle.
/// Persisted to workspace/review-agent/iterations/{date}_{seq}.json.
/// </summary>
public sealed class ReviewIteration
{
    /// <summary>Unique iteration identifier (format: {date}_{seq}, e.g., 2026-01-20_001).</summary>
    public required string IterationId { get; init; }

    /// <summary>When the iteration started.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the iteration completed (null if in progress).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Total knowledge nodes reviewed.</summary>
    public int NodesReviewed { get; set; }

    /// <summary>Nodes that passed verification.</summary>
    public int NodesValid { get; set; }

    /// <summary>Nodes that failed verification.</summary>
    public int NodesDeactivated { get; set; }

    /// <summary>Deactivated nodes removed in cleanup.</summary>
    public int NodesRemoved { get; set; }

    /// <summary>Detailed log entries for each node reviewed.</summary>
    public List<ReviewLogEntry> Entries { get; init; } = [];

    /// <summary>
    /// Create summary for list response.
    /// </summary>
    public ReviewIterationSummary ToSummary() => new()
    {
        IterationId = IterationId,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        NodesReviewed = NodesReviewed,
        NodesValid = NodesValid,
        NodesDeactivated = NodesDeactivated,
        NodesRemoved = NodesRemoved
    };
}

/// <summary>
/// Summary of a review iteration for list responses.
/// </summary>
public sealed class ReviewIterationSummary
{
    public required string IterationId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int NodesReviewed { get; set; }
    public int NodesValid { get; set; }
    public int NodesDeactivated { get; set; }
    public int NodesRemoved { get; set; }
}

/// <summary>
/// Response for paginated iteration list.
/// </summary>
public sealed class IterationListResponse
{
    public required List<ReviewIterationSummary> Iterations { get; init; }
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}
