using System.Text.Json.Serialization;
using VibeResearching.Vibe.ReviewAgent;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Events;

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
/// Emitted when the Review Agent status changes.
/// </summary>
public sealed class StatusChangeEvent : ReviewAgentEvent
{
    public override string Type => "status_change";

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("nextScheduledAt")]
    public string? NextScheduledAt { get; init; }
}

/// <summary>
/// Emitted when a node review completes.
/// </summary>
public sealed class NodeReviewProgressEvent : ReviewAgentEvent
{
    public override string Type => "node_review_progress";

    [JsonPropertyName("nodeId")]
    public required string NodeId { get; init; }

    [JsonPropertyName("nodeLabel")]
    public required string NodeLabel { get; init; }

    [JsonPropertyName("explainContent")]
    public string? ExplainContent { get; init; }

    [JsonPropertyName("nodesReviewed")]
    public int NodesReviewed { get; init; }

    [JsonPropertyName("nodesPending")]
    public int NodesPending { get; init; }

    [JsonPropertyName("nodesDeactivated")]
    public int NodesDeactivated { get; init; }

    [JsonPropertyName("result")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReviewResult? Result { get; init; }
}

/// <summary>
/// Emitted during LLM token streaming.
/// </summary>
public sealed class TokenStreamEvent : ReviewAgentEvent
{
    public override string Type => "token_stream";

    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    [JsonPropertyName("agentRole")]
    public required string AgentRole { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; init; }
}

/// <summary>
/// Emitted when an iteration completes.
/// </summary>
public sealed class IterationCompleteEvent : ReviewAgentEvent
{
    public override string Type => "iteration_complete";

    [JsonPropertyName("iterationId")]
    public required string IterationId { get; init; }

    [JsonPropertyName("summary")]
    public required ReviewIterationSummary Summary { get; init; }
}

/// <summary>
/// Emitted during cleanup rounds.
/// </summary>
public sealed class CleanupProgressEvent : ReviewAgentEvent
{
    public override string Type => "cleanup_progress";

    [JsonPropertyName("nodesRemoved")]
    public int NodesRemoved { get; init; }

    [JsonPropertyName("removedNodes")]
    public List<RemovedNodeInfo> RemovedNodes { get; init; } = [];
}

/// <summary>
/// Info about a removed node.
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
