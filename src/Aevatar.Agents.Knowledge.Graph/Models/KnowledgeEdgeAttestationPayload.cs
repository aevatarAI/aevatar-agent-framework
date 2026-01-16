using System.Text;

namespace Aevatar.Agents.Knowledge.Graph.Models;

/// <summary>
/// Canonical payload builder for node attestations.
/// <para>
/// This library intentionally does NOT implement cryptography; it only standardizes what should be signed.
/// </para>
/// </summary>
public static class KnowledgeNodeAttestationPayload
{
    /// <summary>
    /// Builds a canonical UTF-8 payload string to be signed for attesting that a node (knowledge item) is correct.
    /// <para>
    /// Design goals:
    /// - Cross-language stable
    /// - No ambiguity (explicit keys)
    /// - Replay-resistant across different graphs/sessions/nodes
    /// </para>
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="nodeId">The node ID.</param>
    /// <param name="nodeType">The node type ("Plan" or "Knowledge").</param>
    public static string Build(
        string sessionId,
        string nodeId,
        string nodeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        // ASCII-only, newline-delimited, fixed key order.
        // NOTE: Do not change keys/order without version bump.
        var sb = new StringBuilder();
        sb.AppendLine("aevatar.knowledge-graph.node-attestation/v2");
        sb.Append("sessionId=").AppendLine(sessionId.Trim());
        sb.Append("nodeId=").AppendLine(nodeId.Trim());
        sb.Append("nodeType=").AppendLine(nodeType.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Builds attestation payload for a graph node.
    /// </summary>
    public static string Build(IGraphNode node)
    {
        var nodeType = node switch
        {
            PlanNode => "Plan",
            KnowledgeNode => "Knowledge",
            _ => "Unknown"
        };
        return Build(node.SessionId, node.Id, nodeType);
    }
}