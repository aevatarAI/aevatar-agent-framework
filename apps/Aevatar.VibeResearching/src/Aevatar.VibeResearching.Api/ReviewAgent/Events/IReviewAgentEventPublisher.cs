using VibeResearching.Vibe.ReviewAgent;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Events;

/// <summary>
/// Interface for publishing Review Agent SSE events.
/// </summary>
public interface IReviewAgentEventPublisher
{
    /// <summary>
    /// Publish a status change event.
    /// </summary>
    Task PublishStatusChangeAsync(ReviewAgentStatus status, DateTimeOffset? nextScheduledAt = null);

    /// <summary>
    /// Publish a node review progress event.
    /// </summary>
    Task PublishNodeProgressAsync(
        string nodeId,
        string nodeLabel,
        string? explainContent,
        int nodesReviewed,
        int nodesPending,
        int nodesDeactivated,
        ReviewResult? result = null);

    /// <summary>
    /// Publish a token stream event from the verification process.
    /// </summary>
    Task PublishTokenAsync(string agentId, string agentRole, string token, bool isComplete);

    /// <summary>
    /// Publish an iteration complete event.
    /// </summary>
    Task PublishIterationCompleteAsync(string iterationId, ReviewIterationSummary summary);

    /// <summary>
    /// Publish a cleanup progress event.
    /// </summary>
    Task PublishCleanupProgressAsync(int nodesRemoved, List<RemovedNodeInfo> removedNodes);

    /// <summary>
    /// Add a subscriber for SSE events.
    /// </summary>
    IDisposable Subscribe(Func<ReviewAgentEvent, Task> handler);
}
