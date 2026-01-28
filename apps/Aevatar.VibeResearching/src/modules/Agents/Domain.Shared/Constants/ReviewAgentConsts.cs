namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Constants for Review Agent operations.
/// </summary>
public static class ReviewAgentConsts
{
    /// <summary>
    /// Default iteration interval in minutes between review rounds.
    /// </summary>
    public const int DefaultIterationIntervalMinutes = 30;

    /// <summary>
    /// Default threshold in minutes before a node is considered stale.
    /// </summary>
    public const int DefaultOutOfDateThresholdMinutes = 60;

    /// <summary>
    /// Default threshold in minutes before a deactivated node is permanently deleted.
    /// </summary>
    public const int DefaultToDeleteThresholdMinutes = 1440; // 24 hours

    /// <summary>
    /// Minimum threshold in minutes for deletion (safety).
    /// </summary>
    public const int MinToDeleteThresholdMinutes = 60; // 1 hour

    /// <summary>
    /// Default timeout in seconds for per-node verification.
    /// </summary>
    public const int DefaultPerNodeTimeoutSeconds = 120; // 2 minutes

    /// <summary>
    /// Configuration section name for review agent options.
    /// </summary>
    public const string ConfigurationSectionName = "ReviewAgent";

    /// <summary>
    /// Default LLM provider name.
    /// </summary>
    public const string DefaultLLMProviderName = "default";
}
