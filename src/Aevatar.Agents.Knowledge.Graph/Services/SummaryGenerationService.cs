using System.Text;
using Aevatar.Agents.Knowledge.Graph.Models;

namespace Aevatar.Agents.Knowledge.Graph.Services;

/// <summary>
/// Service for generating deterministic markdown summaries for sessions and DAGs.
/// Per US7, generates summaries that capture the complete research state.
/// </summary>
public interface ISummaryGenerationService
{
    /// <summary>
    /// Generates a session summary showing plan progress and knowledge created.
    /// </summary>
    Task<SessionSummary> GenerateSessionSummaryAsync(
        Session? session,
        IReadOnlyList<PlanNode> planNodes,
        IReadOnlyList<KnowledgeNode> knowledgeNodes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a full DAG summary showing all nodes and relationships.
    /// </summary>
    Task<DagSummary> GenerateFullDagSummaryAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a session summary.
/// </summary>
public sealed record SessionSummary
{
    /// <summary>
    /// The session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Session status (Active/Completed/Abandoned).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Markdown-formatted summary content.
    /// </summary>
    public required string MarkdownContent { get; init; }

    /// <summary>
    /// Number of plan nodes.
    /// </summary>
    public required int PlanNodeCount { get; init; }

    /// <summary>
    /// Number of knowledge nodes.
    /// </summary>
    public required int KnowledgeNodeCount { get; init; }

    /// <summary>
    /// Plan progress percentage (completed / total).
    /// </summary>
    public required double ProgressPercentage { get; init; }
}

/// <summary>
/// Represents a full DAG summary.
/// </summary>
public sealed record DagSummary
{
    /// <summary>
    /// The session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Markdown-formatted summary content.
    /// </summary>
    public required string MarkdownContent { get; init; }

    /// <summary>
    /// Total number of nodes.
    /// </summary>
    public required int TotalNodes { get; init; }

    /// <summary>
    /// Total number of edges.
    /// </summary>
    public required int TotalEdges { get; init; }

    /// <summary>
    /// Maximum depth of the DAG.
    /// </summary>
    public required int MaxDepth { get; init; }
}

/// <summary>
/// Default implementation of ISummaryGenerationService.
/// </summary>
public sealed class SummaryGenerationService : ISummaryGenerationService
{
    public Task<SessionSummary> GenerateSessionSummaryAsync(
        Session? session,
        IReadOnlyList<PlanNode> planNodes,
        IReadOnlyList<KnowledgeNode> knowledgeNodes,
        CancellationToken cancellationToken = default)
    {
        var sessionId = session?.Id ?? "unknown";
        var status = session?.Status.ToString() ?? "Unknown";
        var completedPlans = planNodes.Count(p => p.Status == PlanNodeStatus.Completed);
        var totalPlans = planNodes.Count;
        var progress = totalPlans > 0 ? (double)completedPlans / totalPlans * 100 : 0;

        var sb = new StringBuilder();
        sb.AppendLine("# Session Summary");
        sb.AppendLine();
        sb.AppendLine($"**Session ID**: `{sessionId}`");
        sb.AppendLine($"**Status**: {status}");
        sb.AppendLine($"**Generated**: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Overview section
        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine($"- **Plan Steps**: {totalPlans}");
        sb.AppendLine($"- **Knowledge Nodes**: {knowledgeNodes.Count}");
        sb.AppendLine($"- **Progress**: {progress:F1}% ({completedPlans}/{totalPlans} steps completed)");
        sb.AppendLine();

        // Progress bar visualization
        sb.AppendLine("### Progress");
        sb.AppendLine();
        var completedBars = (int)(progress / 5);
        var remainingBars = 20 - completedBars;
        sb.AppendLine($"`[{'█'.Repeat(completedBars)}{'░'.Repeat(remainingBars)}]` {progress:F1}%");
        sb.AppendLine();

        // Plan progress section
        if (planNodes.Count > 0)
        {
            sb.AppendLine("## Research Plan");
            sb.AppendLine();
            sb.AppendLine("| # | Step | Status | Progress |");
            sb.AppendLine("|---|------|--------|----------|");

            foreach (var plan in planNodes.OrderBy(p => p.SequentialOrder))
            {
                var statusEmoji = plan.Status switch
                {
                    PlanNodeStatus.Pending => "⏳ Pending",
                    PlanNodeStatus.Active => "🔄 Active",
                    PlanNodeStatus.Completed => "✅ Done",
                    _ => "❓"
                };
                var progressText = plan.ProgressText ?? "-";
                sb.AppendLine($"| {plan.SequentialOrder} | {plan.CoreDescription} | {statusEmoji} | {progressText} |");
            }
            sb.AppendLine();
        }

        // Knowledge nodes section
        if (knowledgeNodes.Count > 0)
        {
            sb.AppendLine("## Knowledge Created");
            sb.AppendLine();

            var byType = knowledgeNodes.GroupBy(k => k.NodeType).OrderBy(g => g.Key);
            foreach (var group in byType)
            {
                sb.AppendLine($"### {group.Key} ({group.Count()})");
                sb.AppendLine();
                foreach (var node in group)
                {
                    sb.AppendLine($"- **{node.CoreDescription}** (`{node.Id}`)");
                    if (node.DependsOn.Count > 0)
                    {
                        sb.AppendLine($"  - Depends on: {string.Join(", ", node.DependsOn.Select(d => $"`{d}`"))}");
                    }
                }
                sb.AppendLine();
            }
        }

        // Statistics section
        sb.AppendLine("## Statistics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total Plan Steps | {totalPlans} |");
        sb.AppendLine($"| Completed Steps | {completedPlans} |");
        sb.AppendLine($"| Active Steps | {planNodes.Count(p => p.Status == PlanNodeStatus.Active)} |");
        sb.AppendLine($"| Pending Steps | {planNodes.Count(p => p.Status == PlanNodeStatus.Pending)} |");
        sb.AppendLine($"| Knowledge Nodes | {knowledgeNodes.Count} |");
        sb.AppendLine($"| Axioms/Definitions | {knowledgeNodes.Count(k => k.NodeType == KnowledgeNodeType.MathAxiom)} |");
        sb.AppendLine($"| Theorems/Lemmas | {knowledgeNodes.Count(k => k.NodeType == KnowledgeNodeType.MathTheorem || k.NodeType == KnowledgeNodeType.MathLemma)} |");
        sb.AppendLine();

        return Task.FromResult(new SessionSummary
        {
            SessionId = sessionId,
            Status = status,
            MarkdownContent = sb.ToString(),
            PlanNodeCount = totalPlans,
            KnowledgeNodeCount = knowledgeNodes.Count,
            ProgressPercentage = progress
        });
    }

    public Task<DagSummary> GenerateFullDagSummaryAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var maxDepth = CalculateMaxDepth(snapshot);
        var rootNodes = snapshot.AllNodes.Where(n => n.DependsOn.Count == 0).ToList();
        var leafNodes = GetLeafNodes(snapshot);

        var sb = new StringBuilder();
        sb.AppendLine("# Knowledge Graph Summary");
        sb.AppendLine();
        sb.AppendLine($"**Session ID**: `{snapshot.SessionId}`");
        sb.AppendLine($"**Generated**: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Overview
        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine($"- **Total Nodes**: {snapshot.NodeCount}");
        sb.AppendLine($"- **Total Edges**: {snapshot.EdgeCount}");
        sb.AppendLine($"- **Max Depth**: {maxDepth}");
        sb.AppendLine($"- **Root Nodes**: {rootNodes.Count}");
        sb.AppendLine($"- **Leaf Nodes**: {leafNodes.Count}");
        sb.AppendLine();

        // Node breakdown by kind
        sb.AppendLine("## Node Breakdown");
        sb.AppendLine();
        var planNodes = snapshot.PlanNodes;
        var knowledgeNodes = snapshot.KnowledgeNodes;

        sb.AppendLine($"### Plan Nodes ({planNodes.Count})");
        sb.AppendLine();
        if (planNodes.Count > 0)
        {
            sb.AppendLine("| # | Node | Status | Dependencies |");
            sb.AppendLine("|---|------|--------|--------------|");
            foreach (var node in planNodes.OrderBy(n => n.SequentialOrder))
            {
                var statusEmoji = node.Status switch
                {
                    PlanNodeStatus.Pending => "\u23f3",
                    PlanNodeStatus.Active => "\ud83d\udd04",
                    PlanNodeStatus.Completed => "\u2705",
                    _ => "\u2753"
                };
                var deps = node.DependsOn.Count > 0 ? string.Join(", ", node.DependsOn.Take(3).Select(d => $"`{d}`")) : "-";
                if (node.DependsOn.Count > 3) deps += "...";
                sb.AppendLine($"| {node.SequentialOrder} | {node.CoreDescription} | {statusEmoji} | {deps} |");
            }
        }
        else
        {
            sb.AppendLine("*No plan nodes*");
        }
        sb.AppendLine();

        sb.AppendLine($"### Knowledge Nodes ({knowledgeNodes.Count})");
        sb.AppendLine();
        if (knowledgeNodes.Count > 0)
        {
            var byType = knowledgeNodes.GroupBy(n => n.NodeType).OrderBy(g => g.Key);
            foreach (var group in byType)
            {
                sb.AppendLine($"**{group.Key}** ({group.Count()}):");
                foreach (var node in group)
                {
                    var deps = node.DependsOn.Count > 0 ? $" \u2190 {string.Join(", ", node.DependsOn.Take(2).Select(d => $"`{d}`"))}" : "";
                    if (node.DependsOn.Count > 2) deps += "...";
                    sb.AppendLine($"- `{node.Id}`: {node.CoreDescription}{deps}");
                }
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("*No knowledge nodes*");
        }
        sb.AppendLine();

        // Edge analysis using new EdgeType
        sb.AppendLine("## Edge Analysis");
        sb.AppendLine();
        var dependsOnEdges = snapshot.Edges.Count(e =>
            e.Type == EdgeType.KnowledgeDependsOnKnowledge || e.Type == EdgeType.PlanDependsOnPlan);
        var promotesEdges = snapshot.Edges.Count(e => e.Type == EdgeType.PlanPromotesGoal);
        var motivatedByEdges = snapshot.Edges.Count(e => e.Type == EdgeType.KnowledgeMotivatedByPlan);
        var crossSessionEdges = snapshot.Edges.Count(e => e.Type == EdgeType.CrossSessionReference);

        sb.AppendLine($"- **DependsOn edges**: {dependsOnEdges}");
        sb.AppendLine($"- **Promotes edges**: {promotesEdges}");
        sb.AppendLine($"- **MotivatedBy edges**: {motivatedByEdges}");
        if (crossSessionEdges > 0)
        {
            sb.AppendLine($"- **Cross-session references**: {crossSessionEdges}");
        }
        sb.AppendLine();

        // Topological layers
        sb.AppendLine("## Topological Structure");
        sb.AppendLine();
        var layers = GetTopologicalLayers(snapshot);
        for (var i = 0; i < layers.Count; i++)
        {
            var layerName = i == 0 ? "Foundation (roots)" : i == layers.Count - 1 ? "Conclusions (leaves)" : $"Layer {i}";
            sb.AppendLine($"### {layerName}");
            sb.AppendLine();
            foreach (var nodeId in layers[i])
            {
                var node = snapshot.AllNodes.FirstOrDefault(n => n.Id == nodeId);
                if (node != null)
                {
                    var kindTag = node is PlanNode ? "[Plan]" : "[Knowledge]";
                    sb.AppendLine($"- `{nodeId}` {kindTag}: {node.CoreDescription}");
                }
            }
            sb.AppendLine();
        }

        return Task.FromResult(new DagSummary
        {
            SessionId = snapshot.SessionId,
            MarkdownContent = sb.ToString(),
            TotalNodes = snapshot.NodeCount,
            TotalEdges = snapshot.EdgeCount,
            MaxDepth = maxDepth
        });
    }

    private static int CalculateMaxDepth(GraphSnapshot snapshot)
    {
        var depths = new Dictionary<string, int>();
        var allNodes = snapshot.AllNodes.ToList();
        var nodeSet = allNodes.Select(n => n.Id).ToHashSet();

        int GetDepth(string nodeId)
        {
            if (depths.TryGetValue(nodeId, out var cached)) return cached;

            var node = allNodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null || node.DependsOn.Count == 0)
            {
                depths[nodeId] = 0;
                return 0;
            }

            var maxDepDep = node.DependsOn
                .Where(nodeSet.Contains)
                .Select(GetDepth)
                .DefaultIfEmpty(-1)
                .Max();

            depths[nodeId] = maxDepDep + 1;
            return depths[nodeId];
        }

        foreach (var node in allNodes)
        {
            GetDepth(node.Id);
        }

        return depths.Count > 0 ? depths.Values.Max() : 0;
    }

    private static List<IGraphNode> GetLeafNodes(GraphSnapshot snapshot)
    {
        var hasOutgoing = snapshot.Edges.Select(e => e.ToId).ToHashSet();
        return snapshot.AllNodes.Where(n => !hasOutgoing.Contains(n.Id)).ToList();
    }

    private static List<List<string>> GetTopologicalLayers(GraphSnapshot snapshot)
    {
        var layers = new List<List<string>>();
        var allNodes = snapshot.AllNodes.ToList();
        var remaining = allNodes.Select(n => n.Id).ToHashSet();
        var inDegree = new Dictionary<string, int>();

        // Initialize in-degree
        foreach (var node in allNodes)
        {
            inDegree[node.Id] = node.DependsOn.Count(d => remaining.Contains(d));
        }

        while (remaining.Count > 0)
        {
            // Find nodes with in-degree 0
            var currentLayer = remaining.Where(id => inDegree.GetValueOrDefault(id, 0) == 0).ToList();

            if (currentLayer.Count == 0)
            {
                // Circular dependency or isolated nodes - add remaining
                currentLayer = remaining.ToList();
            }

            layers.Add(currentLayer);

            // Remove current layer and update in-degrees
            foreach (var nodeId in currentLayer)
            {
                remaining.Remove(nodeId);
                foreach (var edge in snapshot.Edges.Where(e => e.ToId == nodeId))
                {
                    if (inDegree.ContainsKey(edge.FromId))
                    {
                        inDegree[edge.FromId]--;
                    }
                }
            }
        }

        return layers;
    }
}

/// <summary>
/// Extension method for string repeat.
/// </summary>
internal static class StringExtensions
{
    public static string Repeat(this char c, int count)
    {
        return count <= 0 ? string.Empty : new string(c, count);
    }
}
