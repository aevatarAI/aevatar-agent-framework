namespace VibeResearching.Vibe.Pivot.Messages;

/// <summary>
/// Response to a rollback request.
/// </summary>
public sealed record RollbackResponse
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Original pivot ID.</summary>
    public required string PivotId { get; init; }

    /// <summary>Whether rollback succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Number of nodes restored.</summary>
    public int RestoredNodeCount { get; init; }

    /// <summary>Number of new nodes preserved (if preserve_new_completed=true).</summary>
    public int PreservedNewNodeCount { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Response timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
