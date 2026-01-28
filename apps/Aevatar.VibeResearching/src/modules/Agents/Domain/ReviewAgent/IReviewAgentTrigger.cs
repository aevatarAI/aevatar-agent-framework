namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Interface for manually triggering Review Agent rounds.
/// The first round requires manual trigger; subsequent rounds are auto-scheduled.
/// </summary>
public interface IReviewAgentTrigger
{
    /// <summary>
    /// Manually trigger a review round.
    /// </summary>
    /// <returns>True if triggered successfully, false if already running.</returns>
    Task<bool> TriggerReviewRoundAsync();

    /// <summary>
    /// Whether a review round is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Whether at least one review round has been completed.
    /// After the first round completes, subsequent rounds are auto-scheduled.
    /// </summary>
    bool HasStarted { get; }
}
