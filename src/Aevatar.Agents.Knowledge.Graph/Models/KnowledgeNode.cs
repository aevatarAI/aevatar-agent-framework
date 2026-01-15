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

    // ========== IGraphNode.Explain() implementation ==========

    /// <inheritdoc />
    public NodeExplanation Explain(GraphSnapshot snapshot)
    {
        var directDeps = DependsOn.ToList();
        var dependents = snapshot.GetDependents(Id).ToList();

        var sb = new StringBuilder();

        // Title
        sb.AppendLine($"# {CoreDescription}");
        sb.AppendLine();

        // Meta info
        sb.AppendLine($"**Type**: {NodeType}");
        sb.AppendLine($"**Node ID**: `{Id}`");
        sb.AppendLine();

        // Description
        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(DetailedDescription);
        sb.AppendLine();

        // Derivation process
        if (!string.IsNullOrWhiteSpace(DerivationProcess))
        {
            sb.AppendLine("## Derivation Process");
            sb.AppendLine();
            sb.AppendLine(DerivationProcess);
            sb.AppendLine();
        }

        // References
        if (References.Count > 0)
        {
            sb.AppendLine("## References");
            sb.AppendLine();
            foreach (var reference in References)
            {
                sb.AppendLine($"- {reference}");
            }
            sb.AppendLine();
        }

        // Dependencies (what this knowledge is derived from)
        if (directDeps.Count > 0)
        {
            sb.AppendLine("## Derived From");
            sb.AppendLine();
            foreach (var depId in directDeps)
            {
                var node = snapshot.GetNode(depId);
                var label = node != null ? $"{node.CoreDescription} (`{depId}`)" : $"`{depId}`";
                sb.AppendLine($"- {label}");
            }
            sb.AppendLine();
        }

        // Dependents (what depends on this knowledge)
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

        // Resource link
        if (!string.IsNullOrWhiteSpace(ResourceUri))
        {
            sb.AppendLine("## Resources");
            sb.AppendLine();
            sb.AppendLine($"[Download Resources]({ResourceUri})");
            sb.AppendLine();
        }

        // Timestamps
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Created: {CreatedAt:yyyy-MM-dd HH:mm:ss UTC}*");

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
}
