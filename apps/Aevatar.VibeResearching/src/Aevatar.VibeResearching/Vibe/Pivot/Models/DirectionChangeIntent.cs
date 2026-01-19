namespace VibeResearching.Vibe.Pivot.Models;

/// <summary>
/// Represents the detected user intent to change research direction.
/// This is a transient value object - not persisted directly.
/// </summary>
public sealed record DirectionChangeIntent
{
    /// <summary>Optional pivot id (used for UI/event correlation).</summary>
    public string? PivotId { get; init; }

    /// <summary>Source user message ID.</summary>
    public required string MessageId { get; init; }

    /// <summary>Research session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Whether this message indicates a direction change intent.</summary>
    public required bool IsDirectionChange { get; init; }

    /// <summary>Confidence score (0.0 - 1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>Extracted new research direction/topic (null if not extracted or ambiguous).</summary>
    public string? NewTopic { get; init; }

    /// <summary>Aspects the user explicitly wants to preserve during the pivot.</summary>
    public IReadOnlyList<string> PreserveAspects { get; init; } = [];

    /// <summary>True if topic extraction was ambiguous and needs user clarification.</summary>
    public bool NeedsClarification { get; init; }

    /// <summary>Detection timestamp (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Validates the intent according to business rules.
    /// </summary>
    public bool IsValid()
    {
        if (Confidence < 0.0 || Confidence > 1.0)
            return false;

        // If it's a direction change with high confidence, NewTopic should be set or clarification needed
        if (IsDirectionChange && Confidence >= 0.7 && string.IsNullOrEmpty(NewTopic) && !NeedsClarification)
            return false;

        return true;
    }
}
