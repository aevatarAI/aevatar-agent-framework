using Microsoft.Extensions.Logging;
using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.VibeResearching.Agents.Pivot.Models;

/// <summary>
/// Wraps a GraphSnapshot with pivot-specific metadata for rollback support.
/// </summary>
public sealed class PivotSnapshotMetadata
{
    /// <summary>Unique snapshot ID.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Session this snapshot belongs to.</summary>
    public required string SessionId { get; init; }

    /// <summary>Pivot operation that triggered this snapshot.</summary>
    public required string PivotId { get; init; }

    /// <summary>The captured graph state (from IKnowledgeGraphClient.GetGraphSnapshotAsync).</summary>
    public required GraphSnapshot Snapshot { get; init; }

    /// <summary>Research direction summary at snapshot time.</summary>
    public required string DirectionSummary { get; init; }

    /// <summary>When the snapshot was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the rollback window expires (default: CreatedAt + 30 minutes).</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Creates a new snapshot metadata with default expiry.
    /// </summary>
    public static PivotSnapshotMetadata Create(
        string sessionId,
        string pivotId,
        GraphSnapshot snapshot,
        string directionSummary,
        int rollbackWindowMinutes = 30)
    {
        var now = DateTimeOffset.UtcNow;
        return new PivotSnapshotMetadata
        {
            SnapshotId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            PivotId = pivotId,
            Snapshot = snapshot,
            DirectionSummary = directionSummary,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(rollbackWindowMinutes)
        };
    }

    /// <summary>
    /// Returns true if this snapshot is still valid for rollback.
    /// </summary>
    public bool IsValid => DateTimeOffset.UtcNow < ExpiresAt;
}
