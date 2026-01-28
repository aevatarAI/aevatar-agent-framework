using System.Text;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Agents;

public sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Plan -> DAG mutation helpers
    // ============================================================

    private static SraDagMutation? BuildPlanDagMutation(
        string sessionId,
        string runId,
        string? question,
        PlanResult plan)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
            return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // Keep plan nodes unique even under shared DAG (multiple sessions writing into one dagId).
        // runId already includes sessionId in "{sessionId}:{seq}" form, so this is collision-resistant.
        var nodeId = SanitizeId($"plan_{runId}");
        if (nodeId.Length == 0)
            nodeId = $"plan_{Guid.NewGuid():N}";

        var label = string.IsNullOrWhiteSpace(plan.RoundTitle)
            ? $"Plan ({Bound(question ?? string.Empty, 120)})"
            : $"Plan: {Bound(plan.RoundTitle!, 180)}";

        var proof = BuildPlanProof(question, plan.Workers);

        var node = new SraDagNode
        {
            Id = nodeId,
            Type = SraDagNodeType.Assumption,
            Label = Bound(label, 200),
            Proof = Bound(proof, 1200),
            UpdatedAt = now,
            Kind = SraDagNodeKind.Plan
        };

        // Tags are optional; graph backend persists Kind separately, but tags are still useful in mutation artifacts.
        node.Tags["runId"] = runId;
        node.Tags["originSessionId"] = sessionId;
        node.Tags["author"] = "research_assistant";
        node.Tags["planKind"] = "round";

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = $"plan_{runId}",
            AuthorAgent = "research_assistant",
            CreatedAt = now
        };
        m.Labels["kind"] = "plan";

        m.UpsertNodes.Add(node);
        // No edges by default: plan nodes are metadata, not derivations.

        return m;
    }

    private static SraDagMutation? BuildMilestonesPlanDagMutation(
        string sessionId,
        string runId,
        string? question,
        SraResearchBriefSnapshot brief)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
            return null;
        if (brief == null || brief.Milestones.Count == 0)
            return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = $"milestones_plan_{runId}",
            AuthorAgent = "research_assistant",
            CreatedAt = now
        };
        m.Labels["kind"] = "plan";
        m.Labels["planKind"] = "milestone";

        // Stable-ish ids: upsert the same milestone nodes across runs for the same session.
        // (If brief is rewritten, these nodes will be updated, not duplicated.)
        var idx = 0;
        foreach (var ms in brief.Milestones.Take(12))
        {
            idx++;
            var roundIndex = ms?.RoundIndex ?? 0;
            var expected = (ms?.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim();
            if (expected.Length == 0) continue;

            var suffix = roundIndex > 0 ? $"r{roundIndex}" : $"i{idx}";
            var nodeId = SanitizeId($"plan_{sessionId}_ms_{suffix}");
            if (nodeId.Length == 0)
                nodeId = $"plan_{Guid.NewGuid():N}";

            var label = roundIndex > 0
                ? $"Milestone (Round {roundIndex}): {Bound(expected, 160)}"
                : $"Milestone: {Bound(expected, 180)}";

            var proofSb = new StringBuilder(256);
            var q = (question ?? string.Empty).Replace("\r", "").Trim();
            if (q.Length > 0) proofSb.AppendLine($"Question: {Bound(q, 600)}");
            if (roundIndex > 0) proofSb.AppendLine($"TargetRound: {roundIndex}");
            proofSb.AppendLine();
            proofSb.AppendLine("ExpectedOutput:");
            proofSb.AppendLine(Bound(expected, 600));

            var node = new SraDagNode
            {
                Id = nodeId,
                Type = SraDagNodeType.Assumption,
                Label = Bound(label, 200),
                Proof = Bound(proofSb.ToString().Trim(), 1200),
                UpdatedAt = now,
                Kind = SraDagNodeKind.Plan
            };

            node.Tags["runId"] = runId;
            node.Tags["originSessionId"] = sessionId;
            node.Tags["author"] = "research_assistant";
            node.Tags["planKind"] = "milestone";
            node.Tags["milestoneRoundIndex"] = roundIndex.ToString();

            m.UpsertNodes.Add(node);
            if (m.UpsertNodes.Count >= 12) break;
        }

        // Create edges to connect milestones in sequential order
        // DAG semantics: fromId (dependency) -> toId (dependent)
        var nodeIds = m.UpsertNodes.Select(n => n.Id).ToList();
        for (var j = 0; j < nodeIds.Count - 1; j++)
        {
            m.UpsertEdges.Add(new SraDagEdge
            {
                FromId = nodeIds[j],
                ToId = nodeIds[j + 1],
                Type = "depends_on",
                UpdatedAt = now
            });
        }

        return m.UpsertNodes.Count == 0 ? null : m;
    }

    private static string BuildPlanProof(string? question, List<PlanWorker>? workers)
    {
        var sb = new StringBuilder(512);
        var q = (question ?? string.Empty).Replace("\r", "").Trim();
        if (q.Length > 0)
            sb.AppendLine($"Question: {Bound(q, 600)}");

        if (workers is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Workers:");
            foreach (var w in workers.Take(12))
            {
                if (w == null) continue;
                var agent = (w.Agent ?? string.Empty).Trim();
                var task = (w.Task ?? string.Empty).Replace("\r", "").Trim();
                if (agent.Length == 0 && task.Length == 0) continue;
                sb.Append("- ").Append(agent.Length == 0 ? "worker" : agent);
                if (task.Length > 0) sb.Append(": ").Append(Bound(task, 260));
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        // keep it bounded to avoid huge ids
        var outId = sb.ToString().Trim('_');
        return outId.Length <= 64 ? outId : outId[..64];
    }
}


