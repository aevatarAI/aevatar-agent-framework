using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Configuration options for the Review Agent.
/// Loaded from appsettings.json and can be overridden at runtime.
/// </summary>
public sealed class ReviewAgentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ReviewAgent";

    /// <summary>
    /// Minutes between review rounds.
    /// Default: 30 minutes
    /// </summary>
    public int IterationIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Minutes before a node is considered stale and requires review.
    /// Default: 60 minutes
    /// </summary>
    public int OutOfDateThresholdMinutes { get; set; } = 60;

    /// <summary>
    /// Minutes before a deactivated node is permanently deleted.
    /// Minimum: 60 minutes (1 hour)
    /// Default: 1440 minutes (24 hours)
    /// </summary>
    public int ToDeleteThresholdMinutes { get; set; } = 1440;

    /// <summary>
    /// Name of the LLM provider to use for verification.
    /// Default: "default"
    /// </summary>
    public string LLMProviderName { get; set; } = "default";

    /// <summary>
    /// Timeout in seconds for per-node verification.
    /// Default: 120 seconds (2 minutes)
    /// </summary>
    public int PerNodeTimeoutSeconds { get; set; } = 120;
}
