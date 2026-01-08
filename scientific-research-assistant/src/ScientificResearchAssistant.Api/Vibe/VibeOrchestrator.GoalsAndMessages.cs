using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Vibe.Brief;
using ScientificResearchAssistant.Api.Vibe.Delivery;
using ScientificResearchAssistant.Api.Vibe.Dag;
using ScientificResearchAssistant.Api.Vibe.Goals;
using ScientificResearchAssistant.Api.Vibe.Trace;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Goals persistence helper (same behavior as Goals API)
    // ============================================================

    private async Task<SraGoalsSnapshot?> TrySaveGoalsAsync(
        ResearchSession session,
        SraGoalsSnapshot existing,
        IReadOnlyList<GoalCandidate> candidates,
        string updatedBy,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var targetVersion = Math.Max(existing.Version + 1, 1);

            var snap = new SraGoalsSnapshot
            {
                SessionId = session.Id,
                Version = targetVersion
            };

            var idx = 0;
            foreach (var g in candidates)
            {
                if (g == null) continue;
                var text = (g.Text ?? string.Empty).Replace("\r", "").Trim();
                if (text.Length == 0) continue;

                idx++;
                var goalId = (g.GoalId ?? string.Empty).Trim();
                if (goalId.Length == 0)
                    goalId = $"g{idx}";

                snap.Goals.Add(new SraGoalItem
                {
                    GoalId = goalId,
                    Text = text,
                    Priority = g.Priority ?? 0
                });

                if (snap.Goals.Count >= 50) break; // bound
            }

            if (snap.Goals.Count == 0)
                return null;

            var saved = await _goals.SaveAsync(session.Id, snap, ct);

            // UI: notify goals updated (best-effort)
            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.goals_updated",
                Value = new { sessionId = session.Id, version = saved.Version, count = saved.Goals.Count }
            });

            // Mailbox broadcast (best-effort) - stable roster for MVP.
            try
            {
                var now = Timestamp.FromDateTime(DateTime.UtcNow);
                var evt = new SraGoalsUpdated
                {
                    SessionId = session.Id,
                    Snapshot = saved,
                    Reason = reason ?? "auto",
                    UpdatedBy = updatedBy ?? "system",
                    CreatedAt = now
                };

                var toAgents = new[] { "research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder" };
                foreach (var a in toAgents)
                {
                    var envelope = new SraMailboxMessage
                    {
                        SessionId = session.Id,
                        MessageId = $"goals_updated:{saved.Version}",
                        FromAgent = updatedBy ?? "system",
                        ToAgent = a,
                        Type = "goals.updated",
                        CorrelationId = $"goals:{saved.Version}",
                        CreatedAt = now,
                        Payload = Any.Pack(evt)
                    };

                    await _mailbox.SendAsync(session.Id, a, envelope, ct);
                }
            }
            catch
            {
                // best-effort only
            }

            return saved;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VibeOrchestrator] SaveGoals failed (best-effort).");
            return null;
        }
    }

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
        SraGoalsSnapshot goals,
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
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).ThenBy(x => x.GoalId, StringComparer.Ordinal).Take(20))
        {
            sb.AppendLine($"- ({g.Priority}) {g.GoalId}: {Bound(g.Text ?? "", 200)}");
        }

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
        SraGoalsSnapshot goals,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> trace,
        List<string>? toAgents,
        List<string>? attachmentPaths)
    {
        // Reuse the plan message style; brief needs the same context.
        return BuildPlanMessage(question, goals, dag, trace, toAgents, attachmentPaths);
    }

    private static string BuildSummaryMessage(
        string question,
        SraGoalsSnapshot goals,
        DagRoundResult dag,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<GoalCandidate> goalSuggestions,
        IReadOnlyList<string> factsWritten)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine($"Question: {question}");
        sb.AppendLine();
        sb.AppendLine("Goals (top):");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(10))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

        if (factsWritten is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Facts written this round (ids/paths):");
            foreach (var p in factsWritten.Take(10))
                sb.AppendLine($"- {Bound(p ?? string.Empty, 240)}");
        }

        if (goalSuggestions is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Goal suggestions (NEED USER CONFIRMATION; not yet applied):");
            foreach (var g in goalSuggestions
                         .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                         .OrderBy(x => x.Priority ?? 0)
                         .Take(12))
            {
                var reason = string.IsNullOrWhiteSpace(g.Reason) ? "" : $" (reason: {Bound(g.Reason!, 160)})";
                sb.AppendLine($"- ({g.Priority ?? 0}) {Bound(g.Text ?? string.Empty, 240)}{reason}");
            }
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
        SraGoalsSnapshot goals,
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
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(12))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

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
        SraGoalsSnapshot goals,
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
        sb.AppendLine("Goals:");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(12))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? "", 220)}");

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
        SraGoalsSnapshot goals,
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
        sb.AppendLine("Goals (top):");
        foreach (var g in goals.Goals.OrderBy(x => x.Priority).Take(10))
            sb.AppendLine($"- ({g.Priority}) {Bound(g.Text ?? string.Empty, 220)}");

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
