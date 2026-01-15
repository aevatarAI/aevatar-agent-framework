using System.Text;
using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// A plan node in the graph representing a tentative research step or milestone.
/// Implements <see cref="IGraphNode"/> for common graph operations.
/// </summary>
public sealed class PlanNode : IGraphNode
{
    /// <summary>Unique identifier within the session (provided by caller).</summary>
    public required string Id { get; init; }

    /// <summary>Session ID for isolation between different sessions.</summary>
    public required string SessionId { get; init; }

    // ========== IGraphNode implementation ==========

    /// <summary>When this node was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When this node was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Owner public key of this node (the agent/user who authored it).
    /// </summary>
    public string? Owner { get; init; }

    // ========== PlanNode-specific fields ==========

    /// <summary>Core description - a concise summary of this plan step.</summary>
    public required string CoreDescription { get; init; }

    /// <summary>Detailed description - comprehensive explanation of this plan step.</summary>
    public required string DetailedDescription { get; init; }

    /// <summary>
    /// Execution status of this plan step.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlanNodeStatus Status { get; init; } = PlanNodeStatus.Pending;

    /// <summary>
    /// Free-text progress description for this plan step.
    /// </summary>
    public string? ProgressText { get; init; }

    /// <summary>
    /// How this plan step will be executed (approach, tools, etc.).
    /// </summary>
    public string? Methodology { get; init; }

    /// <summary>
    /// Position in the plan sequence (1-based).
    /// </summary>
    public int SequentialOrder { get; init; }

    /// <summary>IDs of nodes this plan step depends on (upstream dependencies).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    // ========== Pivot-related fields ==========

    /// <summary>
    /// Status of this node with respect to research direction pivots.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PivotNodeStatus PivotStatus { get; init; } = PivotNodeStatus.Active;

    /// <summary>
    /// When this node was cancelled due to a pivot (null if not cancelled).
    /// </summary>
    public DateTimeOffset? CancelledAt { get; init; }

    /// <summary>
    /// The pivot operation ID that cancelled this node (null if not cancelled).
    /// </summary>
    public string? CancelledByPivotId { get; init; }

    /// <summary>
    /// Research direction context when this node was created.
    /// </summary>
    public string? DirectionContext { get; init; }

    // ========== IGraphNode.Explain() implementation ==========

    /// <inheritdoc />
    public NodeExplanation Explain(GraphSnapshot snapshot)
    {
        var directDeps = DependsOn.ToList();
        var dependents = snapshot.GetDependents(Id).ToList();
        var motivatedKnowledge = snapshot.GetKnowledgeMotivatedByPlan(Id).ToList();

        var sb = new StringBuilder();

        // Title with status badge
        var statusEmoji = Status switch
        {
            PlanNodeStatus.Pending => "\u23f3",  // hourglass
            PlanNodeStatus.Active => "\ud83d\udd04",   // arrows rotating
            PlanNodeStatus.Completed => "\u2705", // check mark
            _ => "\u2753"  // question mark
        };
        sb.AppendLine($"# {statusEmoji} {CoreDescription}");
        sb.AppendLine();

        // Meta info
        sb.AppendLine($"**Status**: {Status}");
        sb.AppendLine($"**Step**: #{SequentialOrder}");
        sb.AppendLine($"**Node ID**: `{Id}`");
        sb.AppendLine();

        // Progress
        if (!string.IsNullOrWhiteSpace(ProgressText))
        {
            sb.AppendLine("## Progress");
            sb.AppendLine();
            sb.AppendLine(ProgressText);
            sb.AppendLine();
        }

        // Description
        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(DetailedDescription);
        sb.AppendLine();

        // Methodology
        if (!string.IsNullOrWhiteSpace(Methodology))
        {
            sb.AppendLine("## Methodology");
            sb.AppendLine();
            sb.AppendLine(Methodology);
            sb.AppendLine();
        }

        // Knowledge produced
        if (motivatedKnowledge.Count > 0)
        {
            sb.AppendLine("## Knowledge Produced");
            sb.AppendLine();
            foreach (var knowledgeId in motivatedKnowledge)
            {
                var node = snapshot.KnowledgeNodes.FirstOrDefault(n => n.Id == knowledgeId);
                var label = node != null ? $"{node.CoreDescription} (`{knowledgeId}`)" : $"`{knowledgeId}`";
                sb.AppendLine($"- {label}");
            }
            sb.AppendLine();
        }

        // Dependencies
        if (directDeps.Count > 0)
        {
            sb.AppendLine("## Dependencies");
            sb.AppendLine();
            foreach (var depId in directDeps)
            {
                var node = snapshot.GetNode(depId);
                var label = node != null ? $"{node.CoreDescription} (`{depId}`)" : $"`{depId}`";
                sb.AppendLine($"- {label}");
            }
            sb.AppendLine();
        }

        // Dependents (what depends on this)
        if (dependents.Count > 0)
        {
            sb.AppendLine("## Used By");
            sb.AppendLine();
            foreach (var depId in dependents)
            {
                var node = snapshot.GetNode(depId);
                var label = node != null ? $"{node.CoreDescription} (`{depId}`)" : $"`{depId}`";
                sb.AppendLine($"- {label}");
            }
            sb.AppendLine();
        }

        // Timestamps
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Created: {CreatedAt:yyyy-MM-dd HH:mm:ss UTC}*");

        return new NodeExplanation
        {
            NodeId = Id,
            NodeType = "Plan",
            Title = CoreDescription,
            MarkdownContent = sb.ToString(),
            DirectDependencies = directDeps,
            Dependents = dependents,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
