namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Context information about an interrupted milestone loop execution.
/// Used to provide context when analyzing user intent and handling interruptions.
/// </summary>
public sealed class InterruptionContext
{
    /// <summary>The run ID that was interrupted.</summary>
    public string InterruptedRunId { get; init; } = string.Empty;

    /// <summary>The new user message that triggered the interruption.</summary>
    public string NewUserMessage { get; init; } = string.Empty;

    /// <summary>When the interruption occurred.</summary>
    public DateTimeOffset InterruptedAt { get; init; }

    /// <summary>Reason for the interruption (e.g., "new_input").</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The milestone index (1-based) where the interruption occurred.</summary>
    public int InterruptedAtMilestoneIndex { get; set; }

    /// <summary>The milestone node ID where the interruption occurred.</summary>
    public string? InterruptedAtMilestoneNodeId { get; set; }

    /// <summary>Total number of milestones in the current plan.</summary>
    public int TotalMilestones { get; set; }

    /// <summary>Number of milestones that have been completed.</summary>
    public int CompletedMilestones { get; set; }
}
