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
using VibeResearching.Api.Vibe.Goals;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe;

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

    private async Task<(SraResearchBriefSnapshot? Brief, AgentPromptRecord? PromptRecord)> TryGetBriefAsync(
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
            var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildBriefMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
            var userMessage = "[MODE:BRIEF]\n" + msg;
            var baseSystemPrompt = VibeResearchAssistantAgent.GetSystemPrompt();
            var materialsContext = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));
            
            // Build final system prompt (with Materials Context appended)
            var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
                ? baseSystemPrompt
                : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:brief"
            };
            req.Context["agent_id"] = raId;
            req.Context["materials_context"] = materialsContext;

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            // Create prompt record
            var promptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: raw,
                Timestamp: DateTimeOffset.UtcNow
            );

            if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
                return (null, promptRecord);

            BriefJson? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<BriefJson>(json!, Json);
            }
            catch
            {
                return (null, promptRecord);
            }

            if (parsed == null)
                return (null, promptRecord);

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

            return (snap, promptRecord);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _host.Logger.LogDebug(ex, "[VibeOrchestrator] research_assistant brief failed (best-effort).");
            
            // Create prompt record even on error
            var errorPromptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: baseSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: $"[Error] {ex.Message}",
                Timestamp: DateTimeOffset.UtcNow
            );
            return (null, errorPromptRecord);
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

    private async Task<(PlanResult Plan, AgentPromptRecord? PromptRecord)> TryGetPlanAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        SraDagSnapshot dag,
        IReadOnlyList<SraRoundSummary> recentTrace,
        string? providerOverride,
        CancellationToken ct)
    {
        var baseSystemPrompt = VibeResearchAssistantAgent.GetSystemPrompt();
        var userMessage = string.Empty;
        var materialsContext = string.Empty;
        
        try
        {
            var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildPlanMessage(question, dag, recentTrace, input.ToAgents, input.AttachmentPaths);
            userMessage = "[MODE:PLAN]\n" + msg;
            materialsContext = MergeGroundedContext(materials.RenderedContext, BuildDagKnowledgeGrounding(dag));
            
            // Build final system prompt (with Materials Context appended)
            var finalSystemPrompt = string.IsNullOrWhiteSpace(materialsContext)
                ? baseSystemPrompt
                : $"{baseSystemPrompt}\n\nMaterials context:\n{materialsContext.Trim()}\n";

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:ra_plan"
            };
            req.Context["agent_id"] = raId;
            req.Context["materials_context"] = materialsContext;

            var resp = await ra.ChatAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            
            // Create prompt record
            var promptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: raw,
                Timestamp: DateTimeOffset.UtcNow
            );
            
            if (!TryExtractJson(raw, out var json))
                return (new PlanResult(null, null, null), promptRecord);

            var parsed = JsonSerializer.Deserialize<PlanJson>(json!, Json);
            var workers = parsed?.Workers?
                .Where(w => !string.IsNullOrWhiteSpace(w.Agent))
                .Select(w => new PlanWorker { Agent = w.Agent, Task = w.Task })
                .ToList();
            return (new PlanResult(json, parsed?.RoundTitle, workers), promptRecord);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _host.Logger.LogDebug(ex, "[VibeOrchestrator] research_assistant plan failed (best-effort).");
            
            // Create prompt record even on error
            var errorPromptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: baseSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: materialsContext,
                RawOutput: $"[Error] {ex.Message}",
                Timestamp: DateTimeOffset.UtcNow
            );
            return (new PlanResult(null, null, null), errorPromptRecord);
        }
    }

    private async Task<(string? Summary, AgentPromptRecord? PromptRecord)> TryGetSummaryAsync(
        string sessionId,
        SessionInputInDto input,
        string question,
        DagRoundResult dagResult,
        IReadOnlyDictionary<string, string> outputs,
        IReadOnlyList<string> factsWritten,
        string? providerOverride,
        CancellationToken ct)
    {
        var baseSystemPrompt = VibeResearchAssistantAgent.GetSystemPrompt();
        var userMessage = string.Empty;
        
        try
        {
            var (ra, raId) = await _core.Runtime.GetResearchAssistantAgentAsync(sessionId, providerOverride, ct);
            var msg = BuildSummaryMessage(question, dagResult, outputs, factsWritten);
            userMessage = "[MODE:SUMMARY]\n" + msg;
            
            // Note: SUMMARY mode does NOT include Materials Context
            var finalSystemPrompt = baseSystemPrompt;

            var req = new ChatRequest
            {
                Message = userMessage,
                RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:summary"
            };
            req.Context["agent_id"] = raId;
            // Note: SUMMARY mode does NOT set materials_context

            var resp = await ra.ChatAsync(req, ct);
            var md = (resp.Content ?? string.Empty).Replace("\r", "").Trim();
            var output = md.Length == 0 ? null : Bound(md, 40_000);
            
            // Create prompt record
            var promptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: finalSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: null, // SUMMARY mode does not include Materials Context
                RawOutput: output ?? string.Empty,
                Timestamp: DateTimeOffset.UtcNow
            );
            
            return (output, promptRecord);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _host.Logger.LogDebug(ex, "[VibeOrchestrator] research_assistant summary failed (best-effort).");
            
            // Create prompt record even on error
            var errorPromptRecord = new AgentPromptRecord(
                AgentName: "research_assistant",
                SystemPrompt: baseSystemPrompt,
                UserPrompt: userMessage,
                MaterialsContext: null,
                RawOutput: $"[Error] {ex.Message}",
                Timestamp: DateTimeOffset.UtcNow
            );
            return (null, errorPromptRecord);
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
            .Where(n => n != null && _core.DagGrounding.ShouldIncludeForGrounding(n))
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
