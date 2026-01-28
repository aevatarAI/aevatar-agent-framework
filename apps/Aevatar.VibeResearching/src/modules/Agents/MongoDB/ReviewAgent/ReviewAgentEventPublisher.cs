using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.ReviewAgent;

namespace Aevatar.VibeResearching.Agents.MongoDB.ReviewAgent;

/// <summary>
/// In-memory event publisher for Review Agent SSE events.
/// Uses a simple pub/sub pattern for broadcasting events to subscribers.
/// </summary>
public sealed class ReviewAgentEventPublisher : IReviewAgentEventPublisher
{
    private readonly ConcurrentDictionary<Guid, Func<ReviewAgentEvent, Task>> _subscribers = new();
    private readonly ILogger<ReviewAgentEventPublisher> _logger;

    public ReviewAgentEventPublisher(ILogger<ReviewAgentEventPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task PublishStatusChangeAsync(ReviewAgentStatus status, DateTimeOffset? nextScheduledAt = null)
    {
        var evt = new StatusChangeEvent
        {
            Status = status.ToString(),
            NextScheduledAt = nextScheduledAt?.ToString("O")
        };

        return PublishAsync(evt);
    }

    /// <inheritdoc />
    public Task PublishNodeProgressAsync(
        string nodeId,
        string nodeLabel,
        string? explainContent,
        int nodesReviewed,
        int nodesPending,
        int nodesDeactivated,
        ReviewResult? result = null)
    {
        _logger.LogInformation(
            "Publishing node_review_progress: nodeId={NodeId}, reviewed={Reviewed}, pending={Pending}",
            nodeId, nodesReviewed, nodesPending);

        var evt = new NodeReviewProgressEvent
        {
            NodeId = nodeId,
            NodeLabel = nodeLabel,
            ExplainContent = explainContent,
            NodesReviewed = nodesReviewed,
            NodesPending = nodesPending,
            NodesDeactivated = nodesDeactivated,
            Result = result
        };

        return PublishAsync(evt);
    }

    /// <inheritdoc />
    public Task PublishTokenAsync(string agentId, string agentRole, string token, bool isComplete)
    {
        // Skip publishing if essential data is missing
        if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("Skipping token_stream event: agentId={AgentId}, token length={TokenLength}",
                agentId ?? "null", token?.Length ?? 0);
            return Task.CompletedTask;
        }

        var evt = new TokenStreamEvent
        {
            AgentId = agentId,
            AgentRole = agentRole ?? "unknown",
            Token = token,
            IsComplete = isComplete
        };

        return PublishAsync(evt);
    }

    /// <inheritdoc />
    public Task PublishIterationCompleteAsync(string iterationId, ReviewIterationSummary summary)
    {
        var evt = new IterationCompleteEvent
        {
            IterationId = iterationId,
            Summary = summary
        };

        return PublishAsync(evt);
    }

    /// <inheritdoc />
    public Task PublishCleanupProgressAsync(int nodesRemoved, List<RemovedNodeInfo> removedNodes)
    {
        var evt = new CleanupProgressEvent
        {
            NodesRemoved = nodesRemoved,
            RemovedNodes = removedNodes
        };

        return PublishAsync(evt);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Func<ReviewAgentEvent, Task> handler)
    {
        var id = Guid.NewGuid();
        _subscribers.TryAdd(id, handler);
        _logger.LogDebug("New SSE subscriber added: {Id}. Total subscribers: {Count}", id, _subscribers.Count);

        return new Subscription(this, id);
    }

    private async Task PublishAsync(ReviewAgentEvent evt)
    {
        var subscribers = _subscribers.Values.ToList();
        if (subscribers.Count == 0)
        {
            _logger.LogDebug("No SSE subscribers for event {Type}, skipping", evt.Type);
            return;
        }

        _logger.LogInformation("Publishing event {Type} to {Count} subscribers", evt.Type, subscribers.Count);

        var tasks = subscribers.Select(async handler =>
        {
            try
            {
                await handler(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in SSE subscriber handler");
            }
        });

        await Task.WhenAll(tasks);
    }

    private void Unsubscribe(Guid id)
    {
        _subscribers.TryRemove(id, out _);
        _logger.LogDebug("SSE subscriber removed: {Id}. Total subscribers: {Count}", id, _subscribers.Count);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ReviewAgentEventPublisher _publisher;
        private readonly Guid _id;
        private bool _disposed;

        public Subscription(ReviewAgentEventPublisher publisher, Guid id)
        {
            _publisher = publisher;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _publisher.Unsubscribe(_id);
        }
    }
}
