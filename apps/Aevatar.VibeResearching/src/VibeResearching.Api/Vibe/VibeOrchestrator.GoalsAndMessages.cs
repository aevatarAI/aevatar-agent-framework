using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    private static int WorkerOrder(string agent)
    {
        return agent switch
        {
            "planner" => 0,
            "reasoner" => 1,
            "librarian" => 2,
            "verifier" => 3,
            "dag_builder" => 4,
            _ => 99
        };
    }

    // ============================================================
    //  Formatting helpers (messages)
    // ============================================================

    private static string BuildPlanMessage(
        string question,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> trace,
        List<string>? toAgents,
        List<string>? attachmentPaths)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"Question: {question}");

        if (toAgents is { Count: > 0 })
            sb.AppendLine($"RoutingHint.ToAgents: [{string.Join(", ", toAgents.Select(x => x.Trim()).Where(x => x.Length > 0))}]");
        if (attachmentPaths is { Count: > 0 })
            sb.AppendLine($"AttachmentPaths: [{string.Join(", ", attachmentPaths.Select(x => x.Trim()).Where(x => x.Length > 0))}]");

        sb.AppendLine();
        sb.AppendLine("Plan (from DAG plan nodes):");
        sb.AppendLine(BuildPlanContextFromDag(dag));

        sb.AppendLine();
        sb.AppendLine("DagStats:");
        sb.AppendLine($"- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}, updatedAt={(dag.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "")}");

        if (trace.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RecentTrace (titles/excerpts):");
            foreach (var t in trace.TakeLast(3))
            {
                sb.AppendLine($"- round={t.RoundIndex}, run={t.RunId}, agents={t.PerAgent.Count}, dagChanges={t.DagChanges.Count}");
            }
        }

        return sb.ToString();
    }

    private static string BuildBriefMessage(
        string question,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> trace,
        List<string>? toAgents,
        List<string>? attachmentPaths)
    {
        // Reuse the plan message style; brief needs the same context.
        return BuildPlanMessage(question, dag, trace, toAgents, attachmentPaths);
    }

    private static string BuildSummaryMessage(
        string question,
        DagRoundResult dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<string> factsWritten)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine($"Question: {question}");
        sb.AppendLine();
        sb.AppendLine("Plan: (see DAG plan nodes)");

        if (factsWritten is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("DAG facts written this round (ids):");
            foreach (var p in factsWritten.Take(10))
                sb.AppendLine($"- {Bound(p ?? string.Empty, 240)}");
        }

        sb.AppendLine();
        sb.AppendLine("DAG outcome:");
        if (dag.Accepted && dag.AcceptedMutation != null)
            sb.AppendLine($"- accepted mutationId={dag.AcceptedMutation.MutationId} nodes={dag.AcceptedMutation.UpsertNodes.Count} edges={dag.AcceptedMutation.UpsertEdges.Count}");
        else if (dag.Blocked)
            sb.AppendLine($"- blocked redFlags=[{string.Join(", ", dag.RedFlags)}] staged={dag.StagedPath ?? ""}");
        else
            sb.AppendLine("- no candidate");

        sb.AppendLine();
        sb.AppendLine("Worker outputs (excerpts):");
        foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{k}]");
            sb.AppendLine(Bound(v ?? "", 2500));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildPlanContextFromDag(SraDagSnapshot dag)
    {
        dag ??= new SraDagSnapshot();

        // Keep prompt bounded; "plan" is an executable hint, not a long narrative.
        static int SafeInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return int.TryParse(s.Trim(), out var x) ? x : 0;
        }

        bool IsMilestone(SraDagNode n) =>
            n.Tags != null &&
            n.Tags.TryGetValue("planKind", out var v) &&
            string.Equals((v ?? string.Empty).Trim(), "milestone", StringComparison.OrdinalIgnoreCase);

        bool IsRoundPlan(SraDagNode n) =>
            n.Tags != null &&
            n.Tags.TryGetValue("planKind", out var v) &&
            string.Equals((v ?? string.Empty).Trim(), "round", StringComparison.OrdinalIgnoreCase);

        var allPlans = dag.Nodes
            .Where(n => n != null && n.Kind == SraDagNodeKind.Plan)
            .ToList();

        var milestones = allPlans
            .Where(n => IsMilestone(n!))
            .OrderBy(n =>
            {
                n!.Tags.TryGetValue("milestoneRoundIndex", out var s);
                var x = SafeInt(s);
                return x <= 0 ? int.MaxValue : x;
            })
            .ThenBy(n => n!.Id, StringComparer.Ordinal)
            .Take(8)
            .ToList();

        var roundPlan = allPlans
            .Where(n => IsRoundPlan(n!))
            .OrderByDescending(n => n!.UpdatedAt?.ToDateTime().ToUniversalTime() ?? DateTime.MinValue)
            .ThenByDescending(n => n!.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var plans = new List<SraDagNode>(capacity: 12);
        plans.AddRange(milestones!);
        if (roundPlan != null) plans.Add(roundPlan);

        if (plans.Count == 0)
            return "(no plan nodes yet)";

        var sb = new StringBuilder(512);
        foreach (var p in plans)
        {
            var id = (p.Id ?? string.Empty).Trim();
            var label = Bound((p.Label ?? string.Empty).Replace("\r", "").Trim(), 220);
            sb.Append("- ").Append(id.Length == 0 ? "plan" : id).Append(": ").Append(label).AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderBriefMarkdown(SraResearchBriefSnapshot brief)
    {
        var sb = new StringBuilder(2048);

        var q = (brief.RewrittenQuestion ?? string.Empty).Replace("\r", "").Trim();
        var scope = (brief.Scope ?? string.Empty).Replace("\r", "").Trim();
        var success = (brief.SuccessCriteria ?? string.Empty).Replace("\r", "").Trim();

        if (q.Length > 0)
        {
            sb.AppendLine("**研究问题改写**");
            sb.AppendLine(q);
            sb.AppendLine();
        }

        if (scope.Length > 0)
        {
            sb.AppendLine("**范围/边界**");
            sb.AppendLine(scope);
            sb.AppendLine();
        }

        if (success.Length > 0)
        {
            sb.AppendLine("**成功标准**");
            sb.AppendLine(success);
            sb.AppendLine();
        }

        if (brief.Assumptions.Count > 0)
        {
            sb.AppendLine("**默认假设**");
            foreach (var a in brief.Assumptions.Take(12))
                sb.AppendLine($"- {a}");
            sb.AppendLine();
        }

        if (brief.Risks.Count > 0)
        {
            sb.AppendLine("**风险点**");
            foreach (var r in brief.Risks.Take(10))
                sb.AppendLine($"- {r}");
            sb.AppendLine();
        }

        if (brief.Uncertainties.Count > 0)
        {
            sb.AppendLine("**不确定点**");
            foreach (var u in brief.Uncertainties.Take(10))
                sb.AppendLine($"- {u}");
            sb.AppendLine();
        }

        if (brief.Milestones.Count > 0)
        {
            sb.AppendLine("**里程碑路线图（按轮次预告）**");
            foreach (var m in brief.Milestones.Take(8))
            {
                var idx = m.RoundIndex <= 0 ? "N" : m.RoundIndex.ToString();
                sb.AppendLine($"- Round {idx}: {m.ExpectedOutput}");
            }
            sb.AppendLine();
        }

        if (brief.Terms.Count > 0)
        {
            sb.AppendLine("**关键术语**");
            foreach (var t in brief.Terms.Take(10))
            {
                if (string.IsNullOrWhiteSpace(t.Term)) continue;
                var meaning = string.IsNullOrWhiteSpace(t.Meaning) ? "" : $" — {t.Meaning}";
                sb.AppendLine($"- {t.Term}{meaning}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("> 你可以直接在聊天里纠偏：改范围/假设/成功标准；或继续让系统进入下一轮。");

        return sb.ToString().TrimEnd();
    }

    private static string BuildWorkerMessage(
        string role,
        string question,
        SraDagSnapshot dag,
        List<string>? attachments,
        string? extra = null)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"Role: {role}");
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Plan:");
        sb.AppendLine(BuildPlanContextFromDag(dag));

        sb.AppendLine();
        sb.AppendLine("DAG stats:");
        sb.AppendLine($"- nodes={dag.Nodes.Count}, edges={dag.Edges.Count}");

        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine();
            sb.AppendLine(extra.Trim());
        }

        return sb.ToString();
    }

    private static string BuildDagBuilderMessage(
        string question,
        SraDagSnapshot dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<LibrarianAxiomCandidate> librarianAxioms,
        List<string>? attachments)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("You must output STRICT JSON ONLY.");
        sb.AppendLine();
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Plan:");
        sb.AppendLine(BuildPlanContextFromDag(dag));

        sb.AppendLine();
        sb.AppendLine($"Current DAG: nodes={dag.Nodes.Count}, edges={dag.Edges.Count}");

        if (librarianAxioms is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Librarian trusted axioms (include as AXIOM nodes when appropriate):");
            try
            {
                // Provide a deterministic, bounded JSON snippet to the model.
                var arr = librarianAxioms
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                    .Take(12)
                    .Select(a => new
                    {
                        id = (a!.Id ?? string.Empty).Trim(),
                        label = Bound(a.Label ?? string.Empty, 200),
                        citation = Bound(a.Citation ?? string.Empty, 300),
                        sourcePath = Bound(a.SourcePath ?? string.Empty, 200),
                        tags = a.Tags != null
                            ? a.Tags.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value!.Trim(), StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>()
                    })
                    .ToList();

                sb.AppendLine(JsonSerializer.Serialize(arr, Json));
            }
            catch
            {
                // best-effort only
            }
        }

        sb.AppendLine();
        sb.AppendLine("Worker outputs (excerpts):");
        foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(k, "dag_builder", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine($"[{k}]");
            sb.AppendLine(Bound(v ?? "", 2200));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildPaperEditorMessage(
        string question,
        DagRoundResult dag,
        IReadOnlyDictionary<string, string> outputs,
        string outlineExcerpt,
        string draftExcerpt,
        List<string>? attachments)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine($"Question: {question}");

        if (attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentPaths:");
            foreach (var p in attachments.Take(12))
                sb.AppendLine($"- {p}");
        }

        sb.AppendLine();
        sb.AppendLine("Plan: (see DAG plan nodes)");

        sb.AppendLine();
        sb.AppendLine("DAG outcome:");
        if (dag.Accepted && dag.AcceptedMutation != null)
            sb.AppendLine($"- accepted mutationId={dag.AcceptedMutation.MutationId} nodes={dag.AcceptedMutation.UpsertNodes.Count} edges={dag.AcceptedMutation.UpsertEdges.Count}");
        else if (dag.Blocked)
            sb.AppendLine($"- blocked redFlags=[{string.Join(", ", dag.RedFlags)}] staged={dag.StagedPath ?? ""}");
        else
            sb.AppendLine("- no candidate");

        sb.AppendLine();
        sb.AppendLine("Current paper outline (excerpt):");
        sb.AppendLine(Bound(outlineExcerpt ?? string.Empty, 8000));

        sb.AppendLine();
        sb.AppendLine("Current paper draft (excerpt):");
        sb.AppendLine(Bound(draftExcerpt ?? string.Empty, 12_000));

        sb.AppendLine();
        sb.AppendLine("Worker outputs (excerpts):");
        foreach (var (k, v) in outputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(k, "dag_builder", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine($"[{k}]");
            sb.AppendLine(Bound(v ?? "", 1800));
            sb.AppendLine();
        }

        sb.AppendLine("Task: Propose delivery center updates. Output Markdown summary + ONE strict JSON object (per your system prompt).");
        return sb.ToString();
    }

    private static async Task<string> SafeReadTextAsync(string path, int maxChars, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            path = (path ?? string.Empty).Trim();
            maxChars = Math.Clamp(maxChars, 0, 200_000);
            if (path.Length == 0 || maxChars == 0)
                return string.Empty;

            if (!File.Exists(path))
                return string.Empty;

            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var sb = new StringBuilder(capacity: Math.Min(maxChars, 4096));
            var buf = new char[2048];
            while (sb.Length < maxChars)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = Math.Min(buf.Length, maxChars - sb.Length);
                var n = await reader.ReadAsync(buf.AsMemory(0, toRead), ct);
                if (n <= 0) break;
                sb.Append(buf, 0, n);
            }

            return sb.ToString().Replace("\r", "");
        }
        catch
        {
            return string.Empty;
        }
    }


}
