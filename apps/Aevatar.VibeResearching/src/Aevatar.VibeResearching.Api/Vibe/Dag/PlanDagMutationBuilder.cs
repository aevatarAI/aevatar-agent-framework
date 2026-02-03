using Google.Protobuf.WellKnownTypes;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Dag;

internal static class PlanDagMutationBuilder
{
    internal sealed record PlanMilestoneItem(int RoundIndex, string ExpectedOutput);

    public static SraDagMutation BuildMilestoneMutation(
        string sessionId,
        string author,
        string source,
        IReadOnlyList<PlanMilestoneItem> items,
        SraDagSnapshot currentDag,
        string proofHeader,
        string proofSource,
        string mutationIdPrefix,
        IReadOnlyList<SraDagNode>? existingMilestones = null,
        bool markRemoved = true)
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var prefix = string.IsNullOrWhiteSpace(mutationIdPrefix) ? "plan" : mutationIdPrefix.Trim();
        var mutationId = $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var mutation = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = mutationId,
            AuthorAgent = string.IsNullOrWhiteSpace(author) ? "planner" : author.Trim(),
            CreatedAt = now
        };
        mutation.Labels["kind"] = "plan";
        mutation.Labels["planKind"] = "milestone";
        mutation.Labels["source"] = string.IsNullOrWhiteSpace(source) ? "planner" : source.Trim();

        var desiredIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new List<string>(capacity: items.Count);

        var index = 0;
        foreach (var item in items)
        {
            index++;
            var suffix = item.RoundIndex > 0 ? $"r{item.RoundIndex}" : $"i{index}";
            var nodeId = SanitizeId($"plan_{sessionId}_ms_{suffix}");
            if (nodeId.Length == 0) nodeId = $"plan_{Guid.NewGuid():N}";

            desiredIds.Add(nodeId);
            nodeIds.Add(nodeId);

            var label = BuildMilestoneLabel(item);
            var proof = BuildMilestoneProof(proofHeader, proofSource, item.ExpectedOutput);

            var node = new SraDagNode
            {
                Id = nodeId,
                Type = SraDagNodeType.Assumption,
                Label = Bound(label, 200),
                Proof = Bound(proof, 1200),
                UpdatedAt = now,
                Kind = SraDagNodeKind.Plan
            };
            node.Tags["originSessionId"] = sessionId;
            node.Tags["author"] = mutation.AuthorAgent ?? "planner";
            node.Tags["planKind"] = "milestone";
            node.Tags["milestoneRoundIndex"] = item.RoundIndex.ToString();
            node.Tags["deleted"] = "false";

            mutation.UpsertNodes.Add(node);
        }

        for (var i = 0; i < nodeIds.Count - 1; i++)
        {
            mutation.UpsertEdges.Add(new SraDagEdge
            {
                FromId = nodeIds[i],
                ToId = nodeIds[i + 1],
                Type = "depends_on",
                UpdatedAt = now
            });
        }

        if (markRemoved)
        {
            var milestones = existingMilestones ?? LoadExistingMilestones(currentDag);
            foreach (var old in milestones)
            {
                if (old == null) continue;
                var id = (old.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                if (desiredIds.Contains(id)) continue;
                old.Tags["deleted"] = "true";
                old.UpdatedAt = now;
                mutation.UpsertNodes.Add(old);
            }
        }

        return mutation;
    }

    public static List<SraDagNode> LoadExistingMilestones(SraDagSnapshot dag)
    {
        dag ??= new SraDagSnapshot();

        bool IsDeleted(SraDagNode n)
        {
            if (n.Tags == null) return false;
            if (!n.Tags.TryGetValue("deleted", out var v)) return false;
            return string.Equals((v ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        bool IsMilestone(SraDagNode n) =>
            !IsDeleted(n) &&
            n.Tags != null &&
            n.Tags.TryGetValue("planKind", out var v) &&
            string.Equals((v ?? string.Empty).Trim(), "milestone", StringComparison.OrdinalIgnoreCase);

        return dag.Nodes
            .Where(n => n != null && n.Kind == SraDagNodeKind.Plan && IsMilestone(n))
            .ToList();
    }

    public static string BuildMilestoneLabel(PlanMilestoneItem item)
    {
        return item.RoundIndex > 0
            ? $"Milestone (Round {item.RoundIndex}): {Bound(item.ExpectedOutput, 160)}"
            : $"Milestone: {Bound(item.ExpectedOutput, 180)}";
    }

    public static string BuildMilestoneProof(string header, string source, string expected)
    {
        var lines = new[]
        {
            $"{(string.IsNullOrWhiteSpace(header) ? "Source" : header)}:",
            Bound(source, 900),
            string.Empty,
            "ExpectedOutput:",
            Bound(expected, 900)
        };
        return string.Join("\n", lines).Trim();
    }

    private static string Bound(string s, int max)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= max) return t;
        return t[..max];
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        var outId = sb.ToString().Trim('_');
        return outId.Length <= 64 ? outId : outId[..64];
    }
}
