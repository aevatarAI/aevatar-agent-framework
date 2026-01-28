using System.Text.Json.Serialization;

namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Event publisher interface for Review Agent SSE events.
/// </summary>
public interface IReviewAgentEventPublisher
{
    /// <summary>
    /// Publish a status change event.
    /// </summary>
    Task PublishStatusChangeAsync(ReviewAgentStatus status, DateTimeOffset? nextScheduledAt = null);

    /// <summary>
    /// Publish node review progress event.
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
    /// Publish streaming token event.
    /// </summary>
    Task PublishTokenAsync(string agentId, string agentRole, string token, bool isComplete);

    /// <summary>
    /// Publish iteration complete event.
    /// </summary>
    Task PublishIterationCompleteAsync(string iterationId, ReviewIterationSummary summary);

    /// <summary>
    /// Publish cleanup progress event.
    /// </summary>
    Task PublishCleanupProgressAsync(int nodesRemoved, List<RemovedNodeInfo> removedNodes);

    /// <summary>
    /// Subscribe to Review Agent events.
    /// </summary>
    IDisposable Subscribe(Func<ReviewAgentEvent, Task> handler);
}

/// <summary>
/// Base class for all Review Agent SSE events.
/// </summary>
public abstract class ReviewAgentEvent
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTimeOffset.UtcNow.ToString("O");
}

/// <summary>
/// Info about a removed node during cleanup.
/// </summary>
public sealed class RemovedNodeInfo
{
    [JsonPropertyName("nodeId")]
    public required string NodeId { get; init; }

    [JsonPropertyName("nodeLabel")]
    public required string NodeLabel { get; init; }

    [JsonPropertyName("deactivatedReason")]
    public required string DeactivatedReason { get; init; }
}
