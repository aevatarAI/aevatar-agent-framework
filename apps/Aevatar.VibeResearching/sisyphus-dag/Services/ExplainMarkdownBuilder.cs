namespace SisyphusDag.Services;

using System.Text;
using SisyphusDag.Models;

/// <summary>
/// Builds the markdown output for the Explain endpoint.
/// Pure static helper -- no state, no dependencies.
/// </summary>
internal static class ExplainMarkdownBuilder
{
    private const int DescriptionTruncateLength = 100;

    /// <summary>
    /// Assembles the full markdown explanation for a knowledge node.
    /// </summary>
    public static string Build(
        KnowledgeNode node,
        List<KnowledgeNode> parentNodes,
        List<KnowledgeNode> childNodes,
        List<(int Level, List<KnowledgeNode> Nodes)> upstreamChain,
        List<(int Level, List<KnowledgeNode> Nodes)> downstreamChain,
        int maxLevel)
    {
        var sb = new StringBuilder();
        AppendInfoSection(sb, node);
        AppendDetailsSection(sb, node);
        AppendDerivationChainSection(sb, parentNodes, childNodes, upstreamChain, downstreamChain, maxLevel);
        return sb.ToString();
    }

    private static void AppendInfoSection(StringBuilder sb, KnowledgeNode node)
    {
        var descriptionCell = TruncateForTable(node.Description, DescriptionTruncateLength);

        sb.AppendLine("## Knowledge Info");
        sb.AppendLine();
        sb.AppendLine("| Property | Value |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| **Id** | `{node.Id}` |");
        sb.AppendLine($"| **Session Id** | `{node.SessionId}` |");
        sb.AppendLine($"| **Title** | {EscapeTableCell(node.Title)} |");
        sb.AppendLine($"| **Description** | {descriptionCell} |");
        sb.AppendLine($"| **References** | {FormatReferences(node.References)} |");
        sb.AppendLine($"| **Resource Uri** | {NoneIfEmpty(node.ResourceUri)} |");
        sb.AppendLine($"| **Is Active** | {node.IsActive} |");
        sb.AppendLine($"| **Last Reviewed At** | {NeverIfEmpty(node.LastReviewedAt)} |");
        sb.AppendLine($"| **Created By** | {EscapeTableCell(node.CreatedBy)} |");
        sb.AppendLine($"| **Created At** | {EscapeTableCell(node.CreatedAt)} |");
        sb.AppendLine($"| **Updated By** | {EscapeTableCell(node.UpdatedBy)} |");
        sb.AppendLine($"| **Updated At** | {EscapeTableCell(node.UpdatedAt)} |");
        sb.AppendLine();
    }

    private static void AppendDetailsSection(StringBuilder sb, KnowledgeNode node)
    {
        sb.AppendLine("## Knowledge Details");
        sb.AppendLine();
        sb.AppendLine("### Description");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(node.Description) ? "*None*" : node.Description);
        sb.AppendLine();
        sb.AppendLine("### Derivation Details");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(node.DeriveDetails) ? "*None*" : node.DeriveDetails);
        sb.AppendLine();
    }

    private static void AppendDerivationChainSection(
        StringBuilder sb,
        List<KnowledgeNode> parentNodes,
        List<KnowledgeNode> childNodes,
        List<(int Level, List<KnowledgeNode> Nodes)> upstreamChain,
        List<(int Level, List<KnowledgeNode> Nodes)> downstreamChain,
        int maxLevel)
    {
        sb.AppendLine("## Knowledge Derivation Chain");
        sb.AppendLine();
        sb.AppendLine("*This section shows the derivation relationships of this knowledge node.*");
        sb.AppendLine();

        AppendParentNodesSection(sb, parentNodes);
        AppendChildNodesSection(sb, childNodes);
        AppendChainSection(sb, $"Full Upstream Chain (BFS, max {maxLevel} levels)", upstreamChain,
            "No further upstream dependencies.");
        AppendChainSection(sb, $"Full Downstream Chain (BFS, max {maxLevel} levels)", downstreamChain,
            "No further downstream dependents.");
    }

    private static void AppendParentNodesSection(
        StringBuilder sb,
        List<KnowledgeNode> parentNodes)
    {
        sb.AppendLine("### Parent Nodes (this node depends on)");
        sb.AppendLine();

        if (parentNodes.Count == 0)
        {
            sb.AppendLine("*This is a foundational knowledge node with no upstream dependencies.*");
        }
        else
        {
            AppendNodeCards(sb, parentNodes);
        }

        sb.AppendLine();
    }

    private static void AppendChildNodesSection(
        StringBuilder sb,
        List<KnowledgeNode> childNodes)
    {
        sb.AppendLine("### Child Nodes (depend on this node)");
        sb.AppendLine();

        if (childNodes.Count == 0)
        {
            sb.AppendLine("*No downstream nodes depend on this knowledge.*");
        }
        else
        {
            AppendNodeCards(sb, childNodes);
        }

        sb.AppendLine();
    }

    private static void AppendChainSection(
        StringBuilder sb,
        string heading,
        List<(int Level, List<KnowledgeNode> Nodes)> chain,
        string emptyMessage)
    {
        sb.AppendLine($"### {heading}");
        sb.AppendLine();

        if (chain.Count == 0)
        {
            sb.AppendLine($"*{emptyMessage}*");
            sb.AppendLine();
            return;
        }

        foreach (var (level, nodes) in chain)
        {
            sb.AppendLine($"#### Level {level}");
            sb.AppendLine();
            AppendNodeCards(sb, nodes);
            sb.AppendLine();
        }
    }

    private static void AppendNodeCards(StringBuilder sb, List<KnowledgeNode> nodes)
    {
        foreach (var node in nodes)
        {
            sb.AppendLine($"##### {EscapeTableCell(node.Title)}");
            sb.AppendLine();
            AppendInfoSection(sb, node);
            if (!string.IsNullOrWhiteSpace(node.DeriveDetails))
            {
                sb.AppendLine("**Derivation Details:**");
                sb.AppendLine();
                sb.AppendLine(node.DeriveDetails);
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }
    }

    private static string EscapeTableCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "*None*";
        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }

    private static string TruncateForTable(string? text, int maxLength)
    {
        var escaped = EscapeTableCell(text);
        if (escaped == "*None*" || escaped.Length <= maxLength)
            return escaped;
        return escaped[..maxLength] + "...";
    }

    private static string FormatReferences(List<string> references)
    {
        if (references.Count == 0) return "*None*";
        return string.Join(", ", references);
    }

    private static string NoneIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "*None*" : EscapeTableCell(value);
    }

    private static string NeverIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "*Never*" : EscapeTableCell(value);
    }
}
