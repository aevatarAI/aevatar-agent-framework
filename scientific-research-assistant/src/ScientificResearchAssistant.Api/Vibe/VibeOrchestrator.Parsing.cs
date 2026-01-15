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
    //  Parsing helpers
    // ============================================================

    private sealed class PlanJson
    {
        public string? RoundTitle { get; init; }
        public List<PlanWorkerJson>? Workers { get; init; }
        public List<string>? Notes { get; init; }
    }

    private sealed class PlanWorkerJson
    {
        public string? Agent { get; init; }
        public string? Task { get; init; }
        public object? Inputs { get; init; }
    }

    private static SraDagMutation? TryParseDagBuilderCandidate(string sessionId, string raw)
    {
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return null;

        DagCandidateJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DagCandidateJson>(json, Json);
        }
        catch
        {
            return null;
        }

        if (parsed == null) return null;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var id = (parsed.MutationId ?? string.Empty).Trim();
        if (id.Length == 0) id = $"dag_builder_{Guid.NewGuid():N}";

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = id,
            AuthorAgent = string.IsNullOrWhiteSpace(parsed.AuthorAgent) ? "dag_builder" : parsed.AuthorAgent.Trim(),
            CreatedAt = now
        };

        // ------------------------------------------------------------
        // Track motivatedBy relationships to create edges later
        //
        // 中文说明：
        // - 收集每个 knowledge node 的 motivatedByPlanNodeId
        // - 后续创建 "motivated_by" 边连接 knowledge node 和 plan node
        // ------------------------------------------------------------
        var motivatedByEdges = new List<(string knowledgeNodeId, string planNodeId)>();

        if (parsed.Nodes != null)
        {
            foreach (var n in parsed.Nodes)
            {
                var nid = (n?.Id ?? string.Empty).Trim();
                if (nid.Length == 0) continue;

                // IMPORTANT: dag_builder can ONLY create Knowledge nodes.
                // Plan nodes are created exclusively by VibeOrchestrator during plan generation.
                // Ignore any "kind" field from LLM output to prevent unauthorized Plan node creation.
                const SraDagNodeKind kind = SraDagNodeKind.Knowledge;

                var node = new SraDagNode
                {
                    Id = nid,
                    Type = ParseNodeType(n.Type),
                    Kind = kind,
                    Label = Bound((n.Label ?? string.Empty).Trim(), 200),
                    Proof = Bound((n.Proof ?? string.Empty).Trim(), 1200),
                    UpdatedAt = now
                };

                if (n.Tags != null)
                {
                    foreach (var kv in n.Tags)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                        node.Tags[kv.Key.Trim()] = Bound(kv.Value?.Trim() ?? "", 200);
                    }
                }

                // Track motivatedByPlanNodeId for edge creation
                var motivatedBy = (n.MotivatedByPlanNodeId ?? string.Empty).Trim();
                if (motivatedBy.Length > 0 && kind == SraDagNodeKind.Knowledge)
                {
                    motivatedByEdges.Add((nid, motivatedBy));
                    // Also store in tags for traceability
                    node.Tags["motivatedByPlanNodeId"] = motivatedBy;
                }

                m.UpsertNodes.Add(node);
            }
        }

        if (parsed.Edges != null)
        {
            foreach (var e in parsed.Edges)
            {
                var from = (e?.From ?? string.Empty).Trim();
                var to = (e?.To ?? string.Empty).Trim();
                if (from.Length == 0 || to.Length == 0) continue;

                m.UpsertEdges.Add(new SraDagEdge
                {
                    FromId = from,
                    ToId = to,
                    Type = string.IsNullOrWhiteSpace(e!.Type) ? "depends_on" : e.Type.Trim(),
                    UpdatedAt = now
                });
            }
        }

        // ------------------------------------------------------------
        // Create motivated_by edges
        //
        // 中文说明：
        // - 语义: knowledgeNode -[motivated_by]-> planNode
        // - 表示该 knowledge 是在执行某个 plan step 时产出的
        // ------------------------------------------------------------
        foreach (var (knowledgeNodeId, planNodeId) in motivatedByEdges)
        {
            m.UpsertEdges.Add(new SraDagEdge
            {
                FromId = knowledgeNodeId,
                ToId = planNodeId,
                Type = "motivated_by",
                UpdatedAt = now
            });
        }

        return m;
    }

    private sealed class DagCandidateJson
    {
        public string? MutationId { get; init; }
        public string? AuthorAgent { get; init; }
        public List<DagNodeJson>? Nodes { get; init; }
        public List<DagEdgeJson>? Edges { get; init; }
    }

    private sealed class DagNodeJson
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Kind { get; init; }  // "plan" or "knowledge"
        public string? Label { get; init; }
        public string? Proof { get; init; }
        public string? MotivatedByPlanNodeId { get; init; }  // For provenance tracking
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed class DagEdgeJson
    {
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Type { get; init; }
    }

    private static SraDagNodeType ParseNodeType(string? s)
    {
        var t = (s ?? string.Empty).Trim().ToLowerInvariant();
        return t switch
        {
            "axiom" => SraDagNodeType.Axiom,
            "theorem" => SraDagNodeType.Theorem,
            "assumption" => SraDagNodeType.Assumption,
            "hypothesis" => SraDagNodeType.Hypothesis,
            "unknown" => SraDagNodeType.Unknown,
            _ => SraDagNodeType.Unknown
        };
    }

    // Best-effort JSON extraction (same spirit as tool runners).
    private static bool TryExtractJson(string stdout, out string? json)
    {
        json = null;
        if (string.IsNullOrWhiteSpace(stdout))
            return false;

        var trimmed = stdout.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            json = trimmed;
            return true;
        }

        var lastEnd = trimmed.LastIndexOf('}');
        if (lastEnd < 0)
        {
            lastEnd = trimmed.LastIndexOf(']');
            if (lastEnd < 0) return false;
        }

        for (var start = lastEnd; start >= 0; start--)
        {
            if (trimmed[start] is '{' or '[')
            {
                var candidate = trimmed.Substring(start, lastEnd - start + 1);
                if (IsValidJson(candidate))
                {
                    json = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValidJson(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    //  Librarian actions (facts write / axioms)
    // ============================================================

    private sealed class LibrarianActionsJson
    {
        public List<FactWriteJson>? FactsWrite { get; init; }
        public List<AxiomForDagJson>? AxiomsForDag { get; init; }
    }

    private sealed class FactWriteJson
    {
        public string? Title { get; init; }
        public string? RelativePath { get; init; }
        public string? Content { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed class AxiomForDagJson
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public string? Citation { get; init; }
        public string? SourcePath { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed record LibrarianFactWrite(string Title, string Content, string? RelativePath, Dictionary<string, string?>? Tags);

    private sealed record LibrarianActions(
        List<LibrarianFactWrite> FactsWrite,
        List<LibrarianAxiomCandidate> AxiomsForDag);

    private static LibrarianActions? TryParseLibrarianActions(string raw)
    {
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return null;

        LibrarianActionsJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LibrarianActionsJson>(json!, Json);
        }
        catch
        {
            return null;
        }

        if (parsed == null)
            return null;

        var facts = new List<LibrarianFactWrite>();
        if (parsed.FactsWrite is { Count: > 0 })
        {
            foreach (var f in parsed.FactsWrite)
            {
                if (f == null) continue;
                var content = (f.Content ?? string.Empty).Replace("\r", "").Trim();
                if (content.Length == 0) continue;

                var title = (f.Title ?? string.Empty).Trim();
                if (title.Length == 0) title = "fact";

                facts.Add(new LibrarianFactWrite(title, content, f.RelativePath, f.Tags));
                if (facts.Count >= 8) break; // bound
            }
        }

        var axioms = new List<LibrarianAxiomCandidate>();
        if (parsed.AxiomsForDag is { Count: > 0 })
        {
            foreach (var a in parsed.AxiomsForDag)
            {
                if (a == null) continue;
                var id = (a.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                var label = (a.Label ?? string.Empty).Replace("\r", "").Trim();
                var citation = (a.Citation ?? string.Empty).Replace("\r", "").Trim();

                axioms.Add(new LibrarianAxiomCandidate
                {
                    Id = id,
                    Label = Bound(label, 220),
                    Citation = Bound(citation, 400),
                    SourcePath = Bound((a.SourcePath ?? string.Empty).Trim(), 240),
                    Tags = a.Tags
                });

                if (axioms.Count >= 20) break;
            }
        }

        if (facts.Count == 0 && axioms.Count == 0)
            return null;

        return new LibrarianActions(facts, axioms);
    }

    private async Task<List<string>> TryWriteFactsAsync(string sessionId, IReadOnlyList<LibrarianFactWrite> facts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (facts.Count == 0) return [];

        var written = new List<string>(capacity: Math.Min(4, facts.Count));
        var max = Math.Clamp(facts.Count, 0, 3);

        for (var i = 0; i < max; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = facts[i];

            try
            {
                var rel = BuildSessionScopedFactPath(sessionId, f.RelativePath, f.Title);

                // If tags exist, prepend a small "meta" header to content (human-readable).
                var content = f.Content;
                if (f.Tags is { Count: > 0 })
                {
                    var kvs = f.Tags
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        .Take(20)
                        .Select(kv => $"- {kv.Key.Trim()}: {kv.Value!.Trim()}");
                    var meta = string.Join('\n', kvs);
                    if (!string.IsNullOrWhiteSpace(meta))
                    {
                        content = $"Meta:\n{meta}\n\n{content}";
                    }
                }

                var saved = await _core.Materials.SaveFactAsync(f.Title, content, rel, ct);
                written.Add(saved.Id);
            }
            catch (Exception ex)
            {
                _host.Logger.LogDebug(ex, "[VibeOrchestrator] librarian fact write failed (best-effort).");
            }
        }

        return written;
    }

    private static string BuildSessionScopedFactPath(string sessionId, string? proposedRelativePath, string title)
    {
        // Always sandbox under sra/{sessionId}/...
        var prefix = $"sra/{sessionId}/";
        var rel = (proposedRelativePath ?? string.Empty).Replace('\\', '/').Trim();
        if (rel.StartsWith("/", StringComparison.Ordinal))
            rel = rel.TrimStart('/');

        if (rel.Length > 0)
        {
            // Remove traversal segments.
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p != "." && p != "..")
                .ToList();
            rel = string.Join('/', parts);
        }

        if (rel.Length == 0)
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            rel = $"facts/{stamp}_{Slugify(title, 48)}.md";
        }

        // Enforce extension.
        var ext = Path.GetExtension(rel);
        if (string.IsNullOrWhiteSpace(ext))
            rel += ".md";

        // Ensure prefix once.
        if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rel = prefix + rel.TrimStart('/');

        return rel;
    }

    private static string Slugify(string input, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 8, 96);
        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0) return "note";

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 64));
        var prevDash = false;
        foreach (var ch in s)
        {
            if (sb.Length >= maxChars) break;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var outSlug = sb.ToString().Trim('-').Trim();
        return outSlug.Length == 0 ? "note" : outSlug;
    }


}
