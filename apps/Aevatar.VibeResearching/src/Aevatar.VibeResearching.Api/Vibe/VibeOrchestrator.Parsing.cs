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

    private static SraDagMutation? TryParseDagBuilderCandidate(
        string sessionId,
        string raw,
        SraDagSnapshot? dag = null,
        string? activeMilestoneId = null)
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
        var mutation = BuildDagMutation(sessionId, parsed, now);
        var motivatedByEdges = new List<(string knowledgeNodeId, string planNodeId)>();

        // Get existing milestone IDs from the DAG for validation
        var existingMilestones = GetExistingMilestoneIds(dag);

        // Use the activeMilestoneId passed from Neo4j query (more reliable than DAG snapshot)
        // Fallback to DAG snapshot if not provided
        var effectiveActiveMilestoneId = activeMilestoneId ?? GetActiveMilestoneId(dag);

        AddDagNodes(parsed, now, mutation, motivatedByEdges, existingMilestones, effectiveActiveMilestoneId);
        AddDagEdges(parsed, now, mutation);
        AddMotivatedByEdges(mutation, now, motivatedByEdges);

        return mutation;
    }

    /// <summary>
    /// Extract existing milestone plan node IDs from the DAG.
    /// Used to validate/fix motivatedByPlanNodeId references in knowledge nodes.
    /// </summary>
    private static HashSet<string> GetExistingMilestoneIds(SraDagSnapshot? dag)
    {
        var milestones = new HashSet<string>(StringComparer.Ordinal);
        if (dag?.Nodes == null) return milestones;

        foreach (var n in dag.Nodes)
        {
            if (n == null) continue;
            if (n.Kind != SraDagNodeKind.Plan) continue;

            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length > 0)
                milestones.Add(id);
        }

        return milestones;
    }

    /// <summary>
    /// Find the currently Active milestone from the DAG.
    /// Returns the node ID of the milestone with PlanStatus == Active, or null if not found.
    /// </summary>
    private static string? GetActiveMilestoneId(SraDagSnapshot? dag)
    {
        if (dag?.Nodes == null) return null;

        foreach (var n in dag.Nodes)
        {
            if (n == null) continue;
            if (n.Kind != SraDagNodeKind.Plan) continue;
            if (n.PlanStatus != SraDagPlanStatus.Active) continue;

            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length > 0)
                return id;
        }

        return null;
    }

    /// <summary>
    /// Find the best matching existing milestone ID, or return the first milestone if no match found.
    /// Priority: exact match > prefix match > any existing milestone
    /// </summary>
    private static string? FindBestMatchingMilestone(string invalidId, HashSet<string> existingMilestones)
    {
        if (existingMilestones.Count == 0)
            return null;

        // Try exact match first (should not happen since we call this when invalid, but be safe)
        if (existingMilestones.Contains(invalidId))
            return invalidId;

        // Try to find a milestone that shares the same session prefix or round index
        // e.g., if invalidId = "plan_abc_123_ms_r2_wrong", look for "plan_abc_123_ms_r2"
        var normalizedInvalid = invalidId.ToLowerInvariant();

        // Sort milestones to prefer later rounds (higher round index) as they are more likely current
        var sortedMilestones = existingMilestones
            .OrderByDescending(m =>
            {
                // Extract round index from milestone ID like "plan_xxx_ms_r2" -> 2
                var idx = m.LastIndexOf("_r", StringComparison.OrdinalIgnoreCase);
                if (idx > 0 && idx + 2 < m.Length && int.TryParse(m[(idx + 2)..].TrimEnd('_'), out var round))
                    return round;
                return 0;
            })
            .ToList();

        // Look for prefix match (e.g., invalidId starts with or is a prefix of existing)
        foreach (var existing in sortedMilestones)
        {
            var normalizedExisting = existing.ToLowerInvariant();
            if (normalizedInvalid.StartsWith(normalizedExisting) ||
                normalizedExisting.StartsWith(normalizedInvalid) ||
                normalizedInvalid.Contains(normalizedExisting) ||
                normalizedExisting.Contains(normalizedInvalid))
            {
                return existing;
            }
        }

        // No match found - return the most recent milestone (highest round index)
        return sortedMilestones.FirstOrDefault();
    }

    // ------------------------------------------------------------
    // String helpers (trim + bound)
    //
    // 中文说明：
    // - 统一处理空值与 Trim，减少重复分支
    // - Bound 仅负责长度裁剪，Trim 负责清理空白
    // ------------------------------------------------------------
    private static string Trimmed(string? value) => (value ?? string.Empty).Trim();
    private static string BoundTrimmed(string? value, int max) => Bound(Trimmed(value), max);

    private static SraDagMutation BuildDagMutation(string sessionId, DagCandidateJson parsed, Timestamp now)
    {
        var id = Trimmed(parsed.MutationId);
        if (id.Length == 0) id = $"dag_builder_{Guid.NewGuid():N}";

        return new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = id,
            AuthorAgent = string.IsNullOrWhiteSpace(parsed.AuthorAgent) ? "dag_builder" : Trimmed(parsed.AuthorAgent),
            CreatedAt = now
        };
    }

    private static void AddDagNodes(
        DagCandidateJson parsed,
        Timestamp now,
        SraDagMutation mutation,
        List<(string knowledgeNodeId, string planNodeId)> motivatedByEdges,
        HashSet<string> existingMilestones,
        string? activeMilestoneId)
    {
        var nodes = parsed.Nodes?.OfType<DagNodeJson>();
        if (nodes is null) return;

        foreach (var n in nodes)
        {
            var nid = Trimmed(n.Id);
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
                Label = BoundTrimmed(n.Label, 200),
                Proof = BoundTrimmed(n.Proof, 1200),
                UpdatedAt = now
            };

            if (n.Tags is { } tags)
            {
                foreach (var kv in tags)
                {
                    var key = Trimmed(kv.Key);
                    if (key.Length == 0) continue;
                    node.Tags[key] = BoundTrimmed(kv.Value, 200);
                }
            }

            // ============================================================
            // CRITICAL: Knowledge nodes MUST ALWAYS have a motivated_by edge
            // to the current Active milestone. This is a hard requirement.
            // ============================================================
            var motivatedBy = Trimmed(n.MotivatedByPlanNodeId);

            // Priority chain for finding the milestone to link to:
            // 1. LLM-provided motivatedByPlanNodeId (if valid)
            // 2. Active milestone from Neo4j query
            // 3. Best matching milestone from existing milestones
            // 4. Most recent milestone (highest round index) as last resort
            if (motivatedBy.Length == 0 || !existingMilestones.Contains(motivatedBy))
            {
                var originalMotivatedBy = motivatedBy;

                // Try activeMilestoneId first
                if (!string.IsNullOrEmpty(activeMilestoneId) && existingMilestones.Contains(activeMilestoneId))
                {
                    motivatedBy = activeMilestoneId;
                    node.Tags["motivatedByPlanNodeId_auto"] = "active_milestone";
                }
                // Try finding best match from existing milestones
                else if (motivatedBy.Length > 0)
                {
                    var bestMatch = FindBestMatchingMilestone(motivatedBy, existingMilestones);
                    if (!string.IsNullOrEmpty(bestMatch))
                    {
                        motivatedBy = bestMatch;
                        node.Tags["motivatedByPlanNodeId_auto"] = "best_match";
                    }
                }
                // Last resort: use the most recent milestone (highest round index)
                else if (existingMilestones.Count > 0)
                {
                    var mostRecentMilestone = FindBestMatchingMilestone("", existingMilestones);
                    if (!string.IsNullOrEmpty(mostRecentMilestone))
                    {
                        motivatedBy = mostRecentMilestone;
                        node.Tags["motivatedByPlanNodeId_auto"] = "most_recent";
                    }
                }

                // Store original for debugging if we had to fix it
                if (originalMotivatedBy.Length > 0 && motivatedBy != originalMotivatedBy)
                {
                    node.Tags["originalMotivatedByPlanNodeId"] = originalMotivatedBy;
                }
            }

            // Create the motivated_by edge (REQUIRED for all Knowledge nodes)
            if (motivatedBy.Length > 0 && kind == SraDagNodeKind.Knowledge)
            {
                motivatedByEdges.Add((nid, motivatedBy));
                node.Tags["motivatedByPlanNodeId"] = motivatedBy;
            }
            else if (kind == SraDagNodeKind.Knowledge && existingMilestones.Count > 0)
            {
                // FALLBACK: If we still don't have a motivatedBy but milestones exist,
                // this is a critical error - log it but still link to any milestone
                var anyMilestone = existingMilestones.First();
                motivatedByEdges.Add((nid, anyMilestone));
                node.Tags["motivatedByPlanNodeId"] = anyMilestone;
                node.Tags["motivatedByPlanNodeId_auto"] = "fallback_any";
            }
            else if (kind == SraDagNodeKind.Knowledge)
            {
                // No milestones exist at all - this can only happen during planning round
                // Tag it for later linking by TryLinkOrphanedKnowledgeNodesToMilestoneAsync
                node.Tags["motivatedByPlanNodeId_pending"] = "true";
            }

            // IMPORTANT: Knowledge node is ALWAYS created, regardless of motivatedBy validity
            mutation.UpsertNodes.Add(node);
        }
    }

    private static void AddDagEdges(DagCandidateJson parsed, Timestamp now, SraDagMutation mutation)
    {
        var edges = parsed.Edges?.OfType<DagEdgeJson>();
        if (edges is null) return;

        foreach (var e in edges)
        {
            var from = Trimmed(e.From);
            var to = Trimmed(e.To);
            if (from.Length == 0 || to.Length == 0) continue;

            mutation.UpsertEdges.Add(new SraDagEdge
            {
                FromId = from,
                ToId = to,
                Type = string.IsNullOrWhiteSpace(e.Type) ? "depends_on" : Trimmed(e.Type),
                UpdatedAt = now
            });
        }
    }

    private static void AddMotivatedByEdges(
        SraDagMutation mutation,
        Timestamp now,
        List<(string knowledgeNodeId, string planNodeId)> motivatedByEdges)
    {
        // ------------------------------------------------------------
        // Create motivated_by edges
        //
        // 中文说明：
        // - 语义: knowledgeNode -[motivated_by]-> planNode
        // - 表示该 knowledge 是在执行某个 plan step 时产出的
        // ------------------------------------------------------------
        foreach (var (knowledgeNodeId, planNodeId) in motivatedByEdges)
        {
            mutation.UpsertEdges.Add(new SraDagEdge
            {
                FromId = knowledgeNodeId,
                ToId = planNodeId,
                Type = "motivated_by",
                UpdatedAt = now
            });
        }
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
    //  Librarian actions (DAG facts / axioms)
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

    private async Task<List<string>> TryWriteFactsAsync(ResearchSession session, IReadOnlyList<LibrarianFactWrite> facts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (facts.Count == 0) return [];

        var max = Math.Clamp(facts.Count, 0, 3);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var mutation = new SraDagMutation
        {
            SessionId = session.Id,
            MutationId = $"librarian_fact_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
            AuthorAgent = "librarian",
            CreatedAt = now
        };

        for (var i = 0; i < max; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = facts[i];

            var id = BuildFactNodeId(f.Title);
            var label = Bound((f.Title ?? string.Empty).Trim(), 200);
            var proof = BuildFactProof(f.Content, f.Tags);

            var node = new SraDagNode
            {
                Id = id,
                Type = SraDagNodeType.Axiom,
                Kind = SraDagNodeKind.Knowledge,
                Label = label.Length == 0 ? id : label,
                Proof = Bound(proof, 2000),
                UpdatedAt = now,
                SessionId = session.Id
            };

            if (f.Tags is { Count: > 0 })
            {
                foreach (var kv in f.Tags)
                {
                    var key = (kv.Key ?? string.Empty).Trim();
                    var val = (kv.Value ?? string.Empty).Trim();
                    if (key.Length == 0) continue;
                    node.Tags[key] = val;
                }
            }

            mutation.UpsertNodes.Add(node);
        }

        if (mutation.UpsertNodes.Count == 0)
            return [];

        try
        {
            await _core.Dag.ApplyMutationAsync(session.EffectiveDagId, mutation, ct);
            return mutation.UpsertNodes
                .Select(n => $"dag:{n.Id}")
                .ToList();
        }
        catch (Exception ex)
        {
            _host.Logger.LogDebug(ex, "[VibeOrchestrator] librarian fact -> dag write failed (best-effort).");
            return [];
        }
    }

    private static string BuildFactNodeId(string title)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var rand = Guid.NewGuid().ToString("N")[..6];
        var slug = Slugify(title, 48);
        return $"fact_{stamp}_{rand}_{slug}";
    }

    private static string BuildFactProof(string content, Dictionary<string, string?>? tags)
    {
        var body = (content ?? string.Empty).Replace("\r", "").Trim();
        if (tags is not { Count: > 0 }) return body;

        var kvs = tags
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Take(20)
            .Select(kv => $"- {kv.Key.Trim()}: {kv.Value!.Trim()}");
        var meta = string.Join('\n', kvs);
        if (string.IsNullOrWhiteSpace(meta)) return body;

        return $"Meta:\n{meta}\n\n{body}";
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
