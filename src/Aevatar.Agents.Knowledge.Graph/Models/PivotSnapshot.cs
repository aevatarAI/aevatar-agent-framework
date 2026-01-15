namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Represents a snapshot of the graph state taken before a research direction pivot.
/// Used for recovery and audit trail per US6.
/// </summary>
public sealed record PivotSnapshot
{
    /// <summary>
    /// Unique identifier for this snapshot.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The session ID this snapshot belongs to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// When the snapshot was taken.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Description of why the pivot was made.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The complete graph snapshot at the time of pivot.
    /// </summary>
    public required KnowledgeSnapshot Snapshot { get; init; }
}
