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
    //  research_assistant helpers
    // ============================================================

    private sealed class BriefJson
    {
        public string? RewrittenQuestion { get; init; }
        public string? Scope { get; init; }
        public string? SuccessCriteria { get; init; }
        public List<BriefTermJson>? Terms { get; init; }
        public List<string>? Assumptions { get; init; }
        public List<string>? Risks { get; init; }
        public List<string>? Uncertainties { get; init; }
        public List<BriefMilestoneJson>? Milestones { get; init; }
    }

    private sealed class BriefTermJson
    {
        public string? Term { get; init; }
        public string? Meaning { get; init; }
    }

    private sealed class BriefMilestoneJson
    {
        public int? RoundIndex { get; init; }
        public string? ExpectedOutput { get; init; }
    }

    private async Task<SraResearchBriefSnapshot?> TryGetBriefAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildBriefMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);

            var req = new ChatRequest
            {
                Message = "[MODE:BRIEF]\n" + msg,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:brief"
            };
            req.Context["agent_id"] = raId;
            req.Context["materials_context"] = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
                return null;

            BriefJson? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<BriefJson>(json!, Json);
            }
            catch
            {
                return null;
            }

            if (parsed == null)
                return null;

            var snap = new SraResearchBriefSnapshot
            {
                SessionId = sessionId,
                Version = 0 // caller sets
            };

            snap.RewrittenQuestion = Bound(parsed.RewrittenQuestion ?? string.Empty, 1200);
            snap.Scope = Bound(parsed.Scope ?? string.Empty, 2000);
            snap.SuccessCriteria = Bound(parsed.SuccessCriteria ?? string.Empty, 1200);

            if (parsed.Terms is { Count: > 0 })
            {
                foreach (var t in parsed.Terms)
                {
                    if (t == null) continue;
                    var term = (t.Term ?? string.Empty).Replace("\r", "").Trim();
                    if (term.Length == 0) continue;
                    snap.Terms.Add(new SraResearchTerm
                    {
                        Term = Bound(term, 80),
                        Meaning = Bound((t.Meaning ?? string.Empty).Replace("\r", "").Trim(), 240)
                    });
                    if (snap.Terms.Count >= 40) break;
                }
            }

            void AddBullets(IEnumerable<string>? items, RepeatedField<string> dst, int max)
            {
                if (items == null) return;
                foreach (var s in items)
                {
                    var v = (s ?? string.Empty).Replace("\r", "").Trim();
                    if (v.Length == 0) continue;
                    dst.Add(Bound(v, 800));
                    if (dst.Count >= max) break;
                }
            }

            AddBullets(parsed.Assumptions, snap.Assumptions, max: 80);
            AddBullets(parsed.Risks, snap.Risks, max: 80);
            AddBullets(parsed.Uncertainties, snap.Uncertainties, max: 80);

            if (parsed.Milestones is { Count: > 0 })
            {
                foreach (var m in parsed.Milestones)
                {
                    if (m == null) continue;
                    var expected = (m.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim();
                    if (expected.Length == 0) continue;
                    snap.Milestones.Add(new SraResearchMilestone
                    {
                        RoundIndex = Math.Clamp(m.RoundIndex ?? 0, 0, 200),
                        ExpectedOutput = Bound(expected, 280)
                    });
                    if (snap.Milestones.Count >= 20) break;
                }
            }

            return snap;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogDebug(ex, "[VibeOrchestrator] research_assistant brief failed (best-effort).");
            return null;
        }
    }

    private sealed record PlanResult(string? RawJson, string? RoundTitle, List<PlanWorker>? Workers);
    private sealed record PlanWorker
    {
        public string? Agent { get; init; }
        public string? Task { get; init; }
    }

    private sealed record LibrarianAxiomCandidate
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public string? Citation { get; init; }
        public string? SourcePath { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private async Task<PlanResult> TryGetPlanAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildPlanMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);

            var req = new ChatRequest
            {
                Message = "[MODE:PLAN]\n" + msg,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:ra_plan"
            };
            req.Context["agent_id"] = raId;
            req.Context["materials_context"] = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            if (!TryExtractJson(raw, out var json))
                return new PlanResult(null, null, null);

            var parsed = JsonSerializer.Deserialize<PlanJson>(json!, Json);
            var workers = parsed?.Workers?
                .Where(w => !string.IsNullOrWhiteSpace(w.Agent))
                .Select(w => new PlanWorker { Agent = w.Agent, Task = w.Task })
                .ToList();
            return new PlanResult(json, parsed?.RoundTitle, workers);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogDebug(ex, "[VibeOrchestrator] research_assistant plan failed (best-effort).");
            return new PlanResult(null, null, null);
        }
    }

    private async Task<string?> TryGetSummaryAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<string> factsWritten,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (ra, raId) = await _runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildSummaryMessage(question, dagResult, outputs, factsWritten);

            var req = new ChatRequest
            {
                Message = "[MODE:SUMMARY]\n" + msg,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:summary"
            };
            req.Context["agent_id"] = raId;

            var resp = await ra.ChatAsync(req, ct);
            var md = (resp.Content ?? string.Empty).Replace("\r", "").Trim();
            return md.Length == 0 ? null : Bound(md, 40_000);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogDebug(ex, "[VibeOrchestrator] research_assistant summary failed (best-effort).");
            return null;
        }
    }

    // ============================================================
    //  Grounded context helpers (MVP)
    // ============================================================

    private static string MergeGroundedContext(string? materialsContext, string? dagKnowledgeContext)
    {
        var a = (materialsContext ?? string.Empty).Trim();
        var b = (dagKnowledgeContext ?? string.Empty).Trim();

        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + "\n\n" + b;
    }

    private string BuildDagKnowledgeGrounding(SraDagSnapshot dag)
    {
        dag ??= new SraDagSnapshot();

        // Keep bounded; this is appended into system prompt.
        const int maxNodes = 80;
        const int maxChars = 6000;

        var nodes = dag.Nodes
            .Where(n => n != null && _dagGrounding.ShouldIncludeForGrounding(n))
            .OrderBy(n => n!.Type)
            .ThenBy(n => n!.Id, StringComparer.Ordinal)
            .Take(maxNodes)
            .ToList();

        if (nodes.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(capacity: 1024);
        sb.AppendLine("DAG grounded knowledge (snapshot excerpt):");
        sb.AppendLine($"- nodesTotal={dag.Nodes.Count}, edgesTotal={dag.Edges.Count}");

        foreach (var n in nodes)
        {
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            var label = Bound((n.Label ?? string.Empty).Replace("\r", "").Trim(), 200);
            var t = n.Type.ToString();
            sb.Append("- ").Append(id).Append(" [").Append(t).Append("]: ").Append(label).AppendLine();
            if (sb.Length >= maxChars) break;
        }

        return Bound(sb.ToString().Trim(), maxChars);
    }
}
