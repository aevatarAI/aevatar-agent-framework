using System.Text.Json.Serialization;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// A knowledge node in the graph representing a piece of scientific knowledge.
/// </summary>
public sealed class KnowledgeNode
{
    /// <summary>Unique identifier within the session (provided by caller).</summary>
    public required string Id { get; init; }

    /// <summary>Session ID for isolation between different sessions.</summary>
    public required string SessionId { get; init; }

    /// <summary>Type of knowledge this node represents.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required KnowledgeNodeType NodeType { get; init; }

    /// <summary>
    /// Whether this node is a tentative plan node or an asserted knowledge node.
    /// <para>Default is <see cref="KnowledgeNodeKind.Knowledge"/> to preserve historical semantics.</para>
    /// </summary>
    public KnowledgeNodeKind Kind { get; init; } = KnowledgeNodeKind.Knowledge;

    /// <summary>
    /// Owner public key of this node (the agent/user who authored it).
    /// <para>
    /// Recommended encoding: hex or base64. Empty/null means "unknown / not set".
    /// </para>
    /// </summary>
    public string? Owner { get; init; }

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

    /// <summary>When this node was created.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Attestations for a knowledge node, as a list of (pubkey, signature).
    /// <para>
    /// For <see cref="KnowledgeNodeKind.Plan"/> this should typically be empty.
    /// </para>
    /// </summary>
    public IReadOnlyList<KnowledgeAttestation> Attestations { get; init; } = [];

    /// <summary>IDs of nodes this node depends on (upstream dependencies).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    // ========== Pivot-related fields (added for research direction pivot feature) ==========

    /// <summary>
    /// Status of this node with respect to research direction pivots.
    /// Default is Active. When a pivot cancels pending nodes, they are marked as Cancelled.
    /// Completed nodes from a previous direction are marked as Superseded.
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
    /// Used for partial pivot filtering to identify which nodes to preserve.
    /// </summary>
    public string? DirectionContext { get; init; }
}
