namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Default pass-through implementation of IReviewAgentTrigger.
/// Returns not-running state until a real review agent runtime is configured.
/// </summary>
public sealed class DefaultReviewAgentTrigger : IReviewAgentTrigger
{
    /// <inheritdoc/>
    public bool IsRunning => false;

    /// <inheritdoc/>
    public bool HasStarted => false;

    /// <inheritdoc/>
    public Task<bool> TriggerReviewRoundAsync() => Task.FromResult(false);
}
