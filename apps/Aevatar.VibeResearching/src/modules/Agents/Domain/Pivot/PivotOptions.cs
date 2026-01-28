using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.Pivot;

/// <summary>
/// Configuration options for the research direction pivot feature.
/// </summary>
public sealed class PivotOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Pivot";

    /// <summary>
    /// Minimum confidence threshold for automatically triggering a pivot.
    /// Default: 0.7
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Confidence threshold below which clarification is requested from user.
    /// Default: 0.6
    /// </summary>
    public double ClarificationThreshold { get; set; } = 0.6;

    /// <summary>
    /// Time window in minutes during which rollback is allowed.
    /// Default: 30 minutes
    /// </summary>
    public int RollbackWindowMinutes { get; set; } = 30;

    /// <summary>
    /// Timeout in seconds for subagent acknowledgments.
    /// Default: 3 seconds (per SC-005)
    /// </summary>
    public int SubagentAckTimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// Maximum number of pivot requests that can be queued per session.
    /// Default: 5
    /// </summary>
    public int MaxQueueDepth { get; set; } = 5;

    /// <summary>
    /// Semantic similarity threshold for "refinement" mode.
    /// When similarity > this threshold, triggers refinement instead of full pivot.
    /// Default: 0.7
    /// </summary>
    public double SemanticSimilarityThreshold { get; set; } = 0.7;
}
