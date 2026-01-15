namespace VibeResearching.Vibe.Pivot.Messages;

/// <summary>
/// Response from a subagent acknowledging a pivot event.
/// </summary>
public sealed record PivotAcknowledgment
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Pivot operation ID being acknowledged.</summary>
    public required string PivotId { get; init; }

    /// <summary>Responding agent identifier.</summary>
    public required string AgentId { get; init; }

    /// <summary>Whether agent acknowledges the pivot.</summary>
    public bool Acknowledged { get; init; } = true;

    /// <summary>Agent status after pivot: "ready", "cancelling", "error".</summary>
    public required string Status { get; init; }

    /// <summary>Optional status message.</summary>
    public string? Message { get; init; }

    /// <summary>Acknowledgment timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
