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
    public static string Build(
        string sessionId,
        string nodeId,
        KnowledgeNodeKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        // ASCII-only, newline-delimited, fixed key order.
        // NOTE: Do not change keys/order without version bump.
        var sb = new StringBuilder();
        sb.AppendLine("aevatar.knowledge-graph.node-attestation/v1");
        sb.Append("sessionId=").AppendLine(sessionId.Trim());
        sb.Append("nodeId=").AppendLine(nodeId.Trim());
        sb.Append("kind=").AppendLine(kind.ToString());
        return sb.ToString();
    }
}