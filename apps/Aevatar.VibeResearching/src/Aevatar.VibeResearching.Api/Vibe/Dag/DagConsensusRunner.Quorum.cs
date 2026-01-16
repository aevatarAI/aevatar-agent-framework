using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Core.Strategies;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using VibeResearching.Api;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;
using VibeResearching.Vibe;

namespace VibeResearching.Api.Vibe.Dag;

public sealed partial class DagConsensusRunner
{
    // ------------------------------------------------------------
    //  verifier-quorum consensus
    // ------------------------------------------------------------

    private sealed record QuorumConfig(int VerifierCount, int Quorum);

    private sealed record QuorumVote(
        string VerifierKey,
        string Focus,
        string AgentId,
        bool Approve,
        IReadOnlyList<string> RedFlags,
        string Notes,
        string ExtractedJson,
        string RawOutputExcerpt,
        string? Error);

    private sealed class QuorumVoteJson
    {
        public bool? Approve { get; init; }
        public List<string>? RedFlags { get; init; }
        public string? Notes { get; init; }
    }

    private async Task<ConsensusResult> RunVerifierQuorumAsync(ConsensusInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(input.SessionId);
        Directory.CreateDirectory(Path.Combine(ws.ArtifactsDir, "dag", "consensus"));

        var cfg = GetQuorumConfig();

        // Fast structural precheck (cheap, deterministic).
        var precheck = PrecheckCandidate(input.Candidate);
        if (precheck.Count > 0)
        {
            var artifact = await WriteQuorumArtifactAsync(ws, input, cfg, votes: [], approveCount: 0, precheckFlags: precheck,
                decision: "blocked_precheck", error: "blocked_by_precheck", ct);
            return new ConsensusResult(false, true, "verifier-quorum", null, precheck, artifact, "blocked_by_precheck");
        }

        var focusTags = new[] { "structure", "grounding", "safety" };
        var votes = new List<QuorumVote>(capacity: cfg.VerifierCount);

        for (var i = 0; i < cfg.VerifierCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            var focus = focusTags[i % focusTags.Length];
            var verifierKey = $"dag_consensus_v{i + 1}_{focus}";
            var (agent, agentId) = await _runtime.GetVerifierAgentAsync(input.SessionId, input.ProviderName, verifierKey, ct);

            var prompt = BuildVerifierPrompt(input, focus);
            var req = new ChatRequest
            {
                Message = prompt,
                RequestId = $"{input.RunId}_dag_consensus_{i + 1}",
                StageHint = "session:vibe:dag_consensus"
            };
            req.Context["agent_id"] = agentId;

            var raw = string.Empty;
            try
            {
                var resp = await agent.ChatAsync(req, ct);
                raw = (resp.Content ?? string.Empty).Replace("\r", "").Trim();
                if (raw.Length > 4000) raw = raw[..4000];

                var parsed = TryParseVote(resp.Content ?? string.Empty, out var extractedJson);
                votes.Add(new QuorumVote(
                    VerifierKey: verifierKey,
                    Focus: focus,
                    AgentId: agentId,
                    Approve: parsed.Approve,
                    RedFlags: parsed.RedFlags,
                    Notes: parsed.Notes,
                    ExtractedJson: extractedJson,
                    RawOutputExcerpt: raw,
                    Error: parsed.Error));
            }
            catch (Exception ex)
            {
                votes.Add(new QuorumVote(
                    VerifierKey: verifierKey,
                    Focus: focus,
                    AgentId: agentId,
                    Approve: false,
                    RedFlags: [],
                    Notes: "",
                    ExtractedJson: "",
                    RawOutputExcerpt: raw,
                    Error: $"verifier_exception: {ex.Message}"));
            }
        }

        var approveCount = votes.Count(v => v.Approve && v.RedFlags.Count == 0);
        var outFlags = votes
            .SelectMany(v => v.RedFlags)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (approveCount < cfg.Quorum && outFlags.Count == 0)
            outFlags.Add("insufficient_quorum");

        var ok = approveCount >= cfg.Quorum && outFlags.Count == 0;
        var decision = ok ? "accepted" : "blocked";

        SraDagMutation? mutation = null;
        if (ok)
            mutation = BuildAcceptedQuorumMutation(ws.SessionId, input.Candidate, cfg, approveCount);

        var artifactPath = await WriteQuorumArtifactAsync(ws, input, cfg, votes, approveCount, precheckFlags: [], decision,
            error: ok ? null : "blocked", ct);

        return new ConsensusResult(ok, !ok, "verifier-quorum", mutation, outFlags, artifactPath, ok ? null : decision);
    }

    private QuorumConfig GetQuorumConfig()
    {
        var count = _configuration.GetValue<int?>("Vibe:DagConsensus:VerifierCount") ?? 3;
        var quorum = _configuration.GetValue<int?>("Vibe:DagConsensus:Quorum") ?? 2;

        count = Math.Clamp(count, 1, 9);
        quorum = Math.Clamp(quorum, 1, count);
        return new QuorumConfig(count, quorum);
    }

    private static List<string> PrecheckCandidate(SraDagMutation candidate)
    {
        var flags = new List<string>();

        // id sanity
        if (string.IsNullOrWhiteSpace(candidate.MutationId))
            flags.Add("missing_mutation_id");
        if (string.IsNullOrWhiteSpace(candidate.AuthorAgent))
            flags.Add("missing_author_agent");

        // self-edge
        foreach (var e in candidate.UpsertEdges)
        {
            if (e == null) continue;
            if (!string.IsNullOrWhiteSpace(e.FromId) &&
                string.Equals(e.FromId.Trim(), e.ToId?.Trim(), StringComparison.Ordinal))
            {
                flags.Add("self_edge");
                break;
            }
        }

        // duplicates (best-effort)
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in candidate.UpsertNodes)
        {
            var id = (n?.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            if (!nodeIds.Add(id))
            {
                flags.Add("duplicate_node_id");
                break;
            }
        }

        return flags;
    }

    private static string BuildVerifierPrompt(ConsensusInput input, string focus)
    {
        // Keep it bounded; verifiers should be cheap.
        var candidateSummary = new
        {
            mutationId = input.Candidate.MutationId,
            authorAgent = input.Candidate.AuthorAgent,
            nodeCount = input.Candidate.UpsertNodes.Count,
            edgeCount = input.Candidate.UpsertEdges.Count,
            nodes = input.Candidate.UpsertNodes.Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                label = n.Label,
                proof = n.Proof,
                tags = n.Tags
            }).Take(30).ToList(),
            edges = input.Candidate.UpsertEdges.Select(e => new { from = e.FromId, to = e.ToId, type = e.Type })
                .Take(60).ToList()
        };

        var currentStats = new
        {
            nodeCount = input.Current.Nodes.Count,
            edgeCount = input.Current.Edges.Count,
            updatedAt = input.Current.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
        };

        var materials = (input.MaterialsContext ?? string.Empty).Replace("\r", "").Trim();
        if (materials.Length > 6000) materials = materials[..6000];

        return $$"""
        You are voting on whether to ACCEPT a DAG mutation into the canonical DAG snapshot.

        Focus:
        - {{focus}}

        Rules:
        - Output STRICT JSON ONLY (no markdown, no code fences).
        - If you see a HARD blocker, set approve=false and add it to redFlags.
        - If you only have minor concerns, keep redFlags empty and explain in notes.
        - Be conservative: do not approve incoherent, cyclic, or malformed mutations.
        - IMPORTANT: missing source files / missing evidence are NOT hard blockers by themselves.
          If grounding is missing, keep redFlags empty and list what's missing in notes.

        What to check (quickly):
        - Structural soundness: ids, missing references, cycles, self-edges, direction (dependency -> dependent).
        - Grounding (if materials are present): does the mutation align with DAG facts?
        - Safety: no unbounded text, no fabricated citations.

        CandidateMutationSummary:
        {{JsonSerializer.Serialize(candidateSummary, Json)}}

        CurrentDagStats:
        {{JsonSerializer.Serialize(currentStats, Json)}}

        MaterialsContext (optional):
        {{materials}}

        Output JSON schema:
        {
          "approve": true,
          "redFlags": ["string"],
          "notes": "string"
        }
        """;
    }

    private static (bool Approve, List<string> RedFlags, string Notes, string ExtractedJson, string? Error) TryParseVote(string raw, out string extractedJson)
    {
        extractedJson = string.Empty;
        if (!TryExtractJson(raw, out var json) || string.IsNullOrWhiteSpace(json))
            return (false, [], "", "", "json_parse_failed");

        extractedJson = Bound(json!, 4000);

        QuorumVoteJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<QuorumVoteJson>(json!, Json);
        }
        catch
        {
            return (false, [], "", extractedJson, "json_deserialize_failed");
        }

        var redFlags = (parsed?.RedFlags ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Take(20)
            .ToList();

        var approve = parsed?.Approve ?? false;
        var notes = Bound((parsed?.Notes ?? string.Empty).Trim(), 1200);

        // "Missing evidence" is a common, non-fatal condition in early research rounds.
        // Treat it as a soft warning (notes) rather than a hard red-flag, otherwise the whole run gets blocked too easily.
        static bool IsSoftMissingEvidenceFlag(string s)
        {
            var t = (s ?? string.Empty).Trim().ToLowerInvariant();
            if (t.Length == 0) return false;
            return (t.Contains("missing evidence") || t.Contains("missing grounding"))
                   && (t.Contains("not found") || t.Contains("cannot confirm") || t.Contains("grounding"));
        }

        var soft = redFlags.Where(IsSoftMissingEvidenceFlag).ToList();
        if (soft.Count > 0)
        {
            redFlags = redFlags.Where(x => !IsSoftMissingEvidenceFlag(x)).ToList();

            // Append soft warnings into notes (bounded).
            var softMsg = "soft_missing_evidence: " + string.Join(" | ", soft.Take(3));
            notes = Bound((notes.Length == 0 ? softMsg : (notes + "\n" + softMsg)).Trim(), 1200);
        }

        // If approve=true but redFlags exist, treat as blocked.
        if (redFlags.Count > 0)
            approve = false;

        return (approve, redFlags, notes, extractedJson, null);
    }

    private static SraDagMutation BuildAcceptedQuorumMutation(string sessionId, SraDagMutation candidate, QuorumConfig cfg, int approveCount)
    {
        var m = candidate.Clone();
        m.SessionId = sessionId;
        m.Labels["consensus_workflow"] = "verifier-quorum";
        m.Labels["consensus_quorum"] = $"{approveCount}/{cfg.VerifierCount} (quorum={cfg.Quorum})";
        return m;
    }

    private async Task<string> WriteQuorumArtifactAsync(
        WorkspacePaths ws,
        ConsensusInput input,
        QuorumConfig cfg,
        IReadOnlyList<QuorumVote> votes,
        int approveCount,
        IReadOnlyList<string> precheckFlags,
        string decision,
        string? error,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dir = Path.Combine(ws.ArtifactsDir, "dag", "consensus");
        Directory.CreateDirectory(dir);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var name = $"quorum_{stamp}_{Guid.NewGuid():N}.json";
        var path = Path.Combine(dir, name);

        var obj = new
        {
            sessionId = ws.SessionId,
            runId = input.RunId,
            workflow = "verifier-quorum",
            config = new { verifierCount = cfg.VerifierCount, quorum = cfg.Quorum },
            decision,
            error = error ?? "",
            precheckFlags = precheckFlags.ToList(),
            approveCount,
            votes = votes.Select(v => new
            {
                verifierKey = v.VerifierKey,
                focus = v.Focus,
                agentId = v.AgentId,
                approve = v.Approve,
                redFlags = v.RedFlags,
                notes = v.Notes,
                extractedJson = v.ExtractedJson,
                rawOutputExcerpt = v.RawOutputExcerpt,
                error = v.Error ?? ""
            }).ToList()
        };

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions(Json) { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);

        return Path.GetRelativePath(ws.SessionRoot, path).Replace('\\', '/').Trim('/');
    }


}
