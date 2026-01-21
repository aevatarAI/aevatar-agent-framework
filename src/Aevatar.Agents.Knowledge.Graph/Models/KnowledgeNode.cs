using System.Text;
using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// A knowledge node in the graph representing a piece of scientific knowledge.
/// Implements <see cref="IGraphNode"/> for common graph operations.
/// </summary>
public sealed class KnowledgeNode : IGraphNode
{
    /// <summary>Unique identifier within the session (provided by caller).</summary>
    public required string Id { get; init; }

    /// <summary>Session ID for isolation between different sessions.</summary>
    public required string SessionId { get; init; }

    /// <summary>Type of knowledge this node represents.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required KnowledgeNodeType NodeType { get; init; }

    // ========== IGraphNode implementation ==========

    /// <summary>When this node was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When this node was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Owner public key of this node (the agent/user who authored it).
    /// </summary>
    public string? Owner { get; init; }

    // ========== KnowledgeNode-specific fields ==========

    /// <summary>Core description - a concise summary of the key conclusion.</summary>
    public required string CoreDescription { get; init; }

    /// <summary>Detailed description - comprehensive explanation of this knowledge.</summary>
    public required string DetailedDescription { get; init; }

    /// <summary>Optional proof in Markdown format.</summary>
    public string? Proof { get; init; }

    /// <summary>Original local folder path for resources (before zip & upload).</summary>
    public string? ResourceFolderPath { get; init; }

    /// <summary>S3 presigned HTTPS URL for resource download.</summary>
    public string? ResourceUri { get; init; }

    /// <summary>
    /// Attestations for a knowledge node, as a list of (pubkey, signature).
    /// </summary>
    public IReadOnlyList<KnowledgeAttestation> Attestations { get; init; } = [];

    /// <summary>IDs of nodes this node depends on (upstream dependencies).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// How this knowledge was derived (methodology, reasoning steps, etc.).
    /// Required: All knowledge must have a derivation source.
    /// </summary>
    public string? DerivationProcess { get; init; }

    /// <summary>
    /// Source references (URLs, papers, citations, etc.).
    /// </summary>
    public IReadOnlyList<string> References { get; init; } = [];

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

    // ========== Review Agent fields ==========

    /// <summary>
    /// When this node was last reviewed by the Review Agent.
    /// Null for nodes that have never been reviewed.
    /// New nodes should set this to CreatedAt.
    /// </summary>
    public DateTimeOffset? LastReviewedAt { get; init; }

    /// <summary>
    /// Whether this node is currently activated (valid).
    /// False = deactivated due to failed verification.
    /// Default: true for all new nodes.
    /// </summary>
    public bool IsActivated { get; init; } = true;

    /// <summary>
    /// Reason why verification failed (null if node is activated).
    /// Set by Review Agent when verification fails.
    /// </summary>
    public string? DeactivatedReason { get; init; }

    /// <summary>
    /// When this node was deactivated (null if node is activated).
    /// Used to determine when node should be permanently deleted.
    /// </summary>
    public DateTimeOffset? DeactivatedTimestamp { get; init; }

    // ========== IGraphNode.Explain() implementation ==========

    /// <inheritdoc />
    public NodeExplanation Explain(GraphSnapshot snapshot)
    {
        var directDeps = DependsOn.ToList();
        var dependents = snapshot.GetDependents(Id).ToList();

        var sb = new StringBuilder();

        // ============================================================
        // Section 1: Knowledge Info (Table)
        // ============================================================
        sb.AppendLine("## Knowledge Info");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| **Id** | `{Id}` |");
        sb.AppendLine($"| **Session Id** | `{SessionId}` |");
        sb.AppendLine($"| **Type** | {NodeType} |");
        sb.AppendLine($"| **Core Description** | {EscapeTableCell(CoreDescription)} |");
        sb.AppendLine($"| **References** | {FormatReferencesForTable()} |");
        sb.AppendLine($"| **Resource Uri** | {(string.IsNullOrWhiteSpace(ResourceUri) ? "*None*" : $"[Download]({ResourceUri})")} |");
        sb.AppendLine($"| **Attestations** | {FormatAttestationsForTable()} |");
        sb.AppendLine($"| **Owner** | {(string.IsNullOrWhiteSpace(Owner) ? "*Not specified*" : $"`{Owner}`")} |");
        sb.AppendLine($"| **Created At** | {CreatedAt:yyyy-MM-dd HH:mm:ss UTC} |");
        sb.AppendLine();

        // ============================================================
        // Section 2: Knowledge Details
        // ============================================================
        sb.AppendLine("## Knowledge Details");
        sb.AppendLine();

        sb.AppendLine("### Detailed Description");
        sb.AppendLine();
        sb.AppendLine(DetailedDescription);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(Proof))
        {
            sb.AppendLine("### Proof");
            sb.AppendLine();
            sb.AppendLine(Proof);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(DerivationProcess))
        {
            sb.AppendLine("### Derivation Process");
            sb.AppendLine();
            sb.AppendLine(DerivationProcess);
            sb.AppendLine();
        }

        // ============================================================
        // Section 3: Knowledge Derivation Chain
        // ============================================================
        sb.AppendLine("## Knowledge Derivation Chain");
        sb.AppendLine();
        sb.AppendLine("*This section shows the complete derivation chain of knowledge nodes that this knowledge is based on.*");
        sb.AppendLine();

        // Build derivation chain - only include KnowledgeNodes, not PlanNodes
        var derivationLevels = BuildDerivationChain(snapshot);

        if (derivationLevels.Count == 0)
        {
            sb.AppendLine("*This is a foundational knowledge node with no upstream dependencies.*");
            sb.AppendLine();
        }
        else
        {
            foreach (var (level, nodes) in derivationLevels)
            {
                sb.AppendLine($"### Derivation Level {level}");
                sb.AppendLine();

                foreach (var node in nodes)
                {
                    var anchor = SanitizeAnchor(node.Id);
                    sb.AppendLine($"#### {node.CoreDescription}");
                    sb.AppendLine();
                    sb.AppendLine($"<a id=\"{anchor}\"></a>");
                    sb.AppendLine();
                    sb.AppendLine("| Property | Value |");
                    sb.AppendLine("|----------|-------|");
                    sb.AppendLine($"| **Id** | `{node.Id}` |");
                    sb.AppendLine($"| **Session Id** | `{node.SessionId}` |");
                    sb.AppendLine($"| **Core Description** | {EscapeTableCell(node.CoreDescription)} |");
                    sb.AppendLine();

                    if (!string.IsNullOrWhiteSpace(node.DetailedDescription))
                    {
                        sb.AppendLine("**Detailed Description:**");
                        sb.AppendLine();
                        sb.AppendLine(node.DetailedDescription);
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrWhiteSpace(node.Proof))
                    {
                        sb.AppendLine("**Proof:**");
                        sb.AppendLine();
                        sb.AppendLine(node.Proof);
                        sb.AppendLine();
                    }

                    if (node.References.Count > 0)
                    {
                        sb.AppendLine("**References:**");
                        sb.AppendLine();
                        foreach (var reference in node.References)
                        {
                            sb.AppendLine($"- {reference}");
                        }
                        sb.AppendLine();
                    }

                    // Based On List - links to parent nodes in this document
                    var parentKnowledgeNodes = GetParentKnowledgeNodes(node, snapshot);
                    if (parentKnowledgeNodes.Count > 0)
                    {
                        sb.AppendLine("**Based On:**");
                        sb.AppendLine();
                        foreach (var parent in parentKnowledgeNodes)
                        {
                            var parentAnchor = SanitizeAnchor(parent.Id);
                            sb.AppendLine($"- [{parent.CoreDescription}](#{parentAnchor})");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }
        }

        return new NodeExplanation
        {
            NodeId = Id,
            NodeType = "Knowledge",
            Title = CoreDescription,
            MarkdownContent = sb.ToString(),
            DirectDependencies = directDeps,
            Dependents = dependents,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    // ========== Helper methods for Explain() ==========

    private string FormatReferencesForTable()
    {
        if (References.Count == 0) return "*None*";
        if (References.Count == 1) return References[0];
        return string.Join(", ", References.Take(3)) + (References.Count > 3 ? $" (+{References.Count - 3} more)" : "");
    }

    private string FormatAttestationsForTable()
    {
        if (Attestations.Count == 0) return "*None*";
        return $"{Attestations.Count} attestation(s)";
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

    private List<(int Level, List<KnowledgeNode> Nodes)> BuildDerivationChain(GraphSnapshot snapshot)
    {
        var result = new List<(int Level, List<KnowledgeNode> Nodes)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { Id };
        var currentLevel = new List<string>(DependsOn);
        var level = 1;

        while (currentLevel.Count > 0)
        {
            var levelNodes = new List<KnowledgeNode>();
            var nextLevel = new List<string>();

            foreach (var nodeId in currentLevel)
            {
                if (visited.Contains(nodeId)) continue;
                visited.Add(nodeId);

                var node = snapshot.GetNode(nodeId);
                // Only include KnowledgeNodes, skip PlanNodes
                if (node is KnowledgeNode kn)
                {
                    levelNodes.Add(kn);
                    // Add this node's dependencies for the next level
                    foreach (var depId in kn.DependsOn)
                    {
                        if (!visited.Contains(depId))
                        {
                            nextLevel.Add(depId);
                        }
                    }
                }
            }

            if (levelNodes.Count > 0)
            {
                result.Add((level, levelNodes));
            }

            currentLevel = nextLevel;
            level++;

            // Safety: prevent infinite loops (max 10 levels)
            if (level > 10) break;
        }

        return result;
    }

    private static List<KnowledgeNode> GetParentKnowledgeNodes(KnowledgeNode node, GraphSnapshot snapshot)
    {
        var result = new List<KnowledgeNode>();
        foreach (var depId in node.DependsOn)
        {
            var parent = snapshot.GetNode(depId);
            if (parent is KnowledgeNode kn)
            {
                result.Add(kn);
            }
        }
        return result;
    }
}
