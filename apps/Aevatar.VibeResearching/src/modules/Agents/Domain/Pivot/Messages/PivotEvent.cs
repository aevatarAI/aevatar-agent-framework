using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.Pivot.Messages;

/// <summary>
/// Event broadcast to subagents when research direction changes.
/// </summary>
public sealed record PivotEvent
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Unique pivot operation ID.</summary>
    public required string PivotId { get; init; }

    /// <summary>Previous research direction summary.</summary>
    public required string OldDirectionSummary { get; init; }

    /// <summary>New research direction.</summary>
    public required string NewDirection { get; init; }

    /// <summary>IDs of nodes being cancelled.</summary>
    public IReadOnlyList<string> CancelledNodeIds { get; init; } = [];

    /// <summary>IDs of nodes being preserved.</summary>
    public IReadOnlyList<string> PreservedNodeIds { get; init; } = [];

    /// <summary>Aspects user explicitly requested to preserve.</summary>
    public IReadOnlyList<string> PreserveAspects { get; init; } = [];

    /// <summary>Event timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
