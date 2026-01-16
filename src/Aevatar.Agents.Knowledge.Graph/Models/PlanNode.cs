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

        var sb = new StringBuilder();

        // ============================================================
        // Section 1: Plan Info (Table)
        // ============================================================
        sb.AppendLine("## Plan Info");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| **Id** | `{Id}` |");
        sb.AppendLine($"| **Session Id** | `{SessionId}` |");
        sb.AppendLine($"| **Core Description** | {EscapeTableCell(CoreDescription)} |");
        sb.AppendLine($"| **Status** | {FormatStatusBadge(Status)} |");
        sb.AppendLine($"| **Progress** | {(string.IsNullOrWhiteSpace(ProgressText) ? "*Not started*" : EscapeTableCell(ProgressText))} |");
        sb.AppendLine($"| **Step Order** | #{SequentialOrder} |");
        sb.AppendLine($"| **Owner** | {(string.IsNullOrWhiteSpace(Owner) ? "*Not specified*" : $"`{Owner}`")} |");
        sb.AppendLine($"| **Created At** | {CreatedAt:yyyy-MM-dd HH:mm:ss UTC} |");
        sb.AppendLine();

        // ============================================================
        // Section 2: Methodology
        // ============================================================
        sb.AppendLine("## Methodology");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(Methodology))
        {
            sb.AppendLine(Methodology);
        }
        else if (!string.IsNullOrWhiteSpace(DetailedDescription))
        {
            sb.AppendLine(DetailedDescription);
        }
        else
        {
            sb.AppendLine("*No methodology specified for this plan step.*");
        }
        sb.AppendLine();

        // ============================================================
        // Section 3: Plan Chain
        // ============================================================
        sb.AppendLine("## Plan Chain");
        sb.AppendLine();
        sb.AppendLine("*This section shows all milestones in the research plan with their execution status.*");
        sb.AppendLine();

        // Get all plan nodes in this session, sorted by sequential order
        var planChain = GetPlanChain(snapshot);

        if (planChain.Count == 0)
        {
            sb.AppendLine("*No plan chain available.*");
            sb.AppendLine();
        }
        else
        {
            foreach (var planNode in planChain)
            {
                var isCurrent = planNode.Id == Id;
                var statusIndicator = GetStatusIndicator(planNode.Status);
                var anchor = SanitizeAnchor(planNode.Id);

                // Highlight current node
                if (isCurrent)
                {
                    sb.AppendLine($"### {statusIndicator} **Milestone {planNode.SequentialOrder}: {planNode.CoreDescription}** (Current)");
                }
                else
                {
                    sb.AppendLine($"### {statusIndicator} Milestone {planNode.SequentialOrder}: {planNode.CoreDescription}");
                }
                sb.AppendLine();
                sb.AppendLine($"<a id=\"{anchor}\"></a>");
                sb.AppendLine();

                sb.AppendLine("| Property | Value |");
                sb.AppendLine("|----------|-------|");
                sb.AppendLine($"| **Id** | `{planNode.Id}` |");
                sb.AppendLine($"| **Status** | {FormatStatusBadge(planNode.Status)} |");

                if (!string.IsNullOrWhiteSpace(planNode.ProgressText))
                {
                    sb.AppendLine($"| **Progress** | {EscapeTableCell(planNode.ProgressText)} |");
                }
                sb.AppendLine();

                // Show methodology summary for non-current nodes (brief)
                if (!isCurrent && !string.IsNullOrWhiteSpace(planNode.Methodology))
                {
                    var methodologySummary = planNode.Methodology.Length > 200
                        ? planNode.Methodology.Substring(0, 200) + "..."
                        : planNode.Methodology;
                    sb.AppendLine("**Methodology:**");
                    sb.AppendLine();
                    sb.AppendLine(EscapeTableCell(methodologySummary));
                    sb.AppendLine();
                }

                // Show knowledge produced by this milestone
                var knowledgeProduced = snapshot.GetKnowledgeMotivatedByPlan(planNode.Id).ToList();
                if (knowledgeProduced.Count > 0)
                {
                    sb.AppendLine("**Knowledge Produced:**");
                    sb.AppendLine();
                    foreach (var knowledgeId in knowledgeProduced)
                    {
                        var knowledgeNode = snapshot.KnowledgeNodes.FirstOrDefault(n => n.Id == knowledgeId);
                        var label = knowledgeNode != null ? knowledgeNode.CoreDescription : knowledgeId;
                        sb.AppendLine($"- {label}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

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

    // ========== Helper methods for Explain() ==========

    private static string FormatStatusBadge(PlanNodeStatus status)
    {
        return status switch
        {
            PlanNodeStatus.Pending => "Pending",
            PlanNodeStatus.Active => "**Active**",
            PlanNodeStatus.Completed => "Completed",
            _ => "Unknown"
        };
    }

    private static string GetStatusIndicator(PlanNodeStatus status)
    {
        return status switch
        {
            PlanNodeStatus.Completed => "[x]",
            PlanNodeStatus.Active => "[>]",
            PlanNodeStatus.Pending => "[ ]",
            _ => "[?]"
        };
    }

    private static string EscapeTableCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "*None*";
        // Escape pipe characters and newlines for table cells
        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }

    private static string SanitizeAnchor(string id)
    {
        // Create a valid HTML anchor from node ID
        return id.Replace("_", "-").Replace(" ", "-").ToLowerInvariant();
    }

    private List<PlanNode> GetPlanChain(GraphSnapshot snapshot)
    {
        // Get all plan nodes in this session, sorted by sequential order
        return snapshot.PlanNodes
            .Where(p => p.SessionId == SessionId)
            .OrderBy(p => p.SequentialOrder)
            .ToList();
    }
}
