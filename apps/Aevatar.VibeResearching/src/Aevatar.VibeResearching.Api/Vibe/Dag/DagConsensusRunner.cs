using System.Text;
using System.Text.Encodings.Web;
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

// ============================================================
//  DagConsensusRunner (consensus gate)
//
//  Goal:
//  - Validate/synthesize a DAG mutation candidate into an accepted mutation, or red-flag it.
//
//  Default:
//  - verifier-quorum (lightweight): N verifiers vote; pass if approvals >= quorum and no hard red-flags.
//
//  Optional:
//  - maker (heavier): Cognitive DSL workflow "maker" refines/normalizes the candidate.
//
//  Output contract (JSON-only, strict):
//  {
//    "mutationId": "string",
//    "author": "string",
//    "nodes": [{ "id":"...", "type":"axiom|theorem|assumption|hypothesis|unknown", "label":"...", "proof":"...", "tags":{...}}],
//    "edges": [{ "from":"...", "to":"...", "type":"depends_on" }],
//    "redFlags": ["..."] // optional
//  }
//
//  Artifacts:
//  - workspace/sessions/{id}/artifacts/dag/consensus/*.json
// ============================================================

public sealed partial class DagConsensusRunner
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping  // 允许中文正常显示，不转义为 Unicode
    };

    private const string MakerWorkflow = "maker";
    private const int MaxRawChars = 30_000;
    private const string ErrorCognitiveExecuteException = "cognitive_execute_exception";
    private const string ErrorMakerFailed = "maker_failed";
    private const string ErrorJsonParseFailed = "json_parse_failed";
    private const string ErrorJsonDeserializeFailed = "json_deserialize_failed";
    private const string ErrorBlockedByRedFlags = "blocked_by_red_flags";
    private const string ErrorEmptyMutation = "empty_mutation";

    private readonly IConfiguration _configuration;
    private readonly ResearchRuntime _runtime;
    private readonly CognitiveStrategy _cognitive;
    private readonly WorkspaceService _workspace;
    private readonly ILogger<DagConsensusRunner> _logger;

    public DagConsensusRunner(
        IConfiguration configuration,
        ResearchRuntime runtime,
        CognitiveStrategy cognitive,
        WorkspaceService workspace,
        ILogger<DagConsensusRunner> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _cognitive = cognitive ?? throw new ArgumentNullException(nameof(cognitive));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public sealed record ConsensusInput(
        string SessionId,
        string RunId,
        SraDagSnapshot Current,
        SraDagMutation Candidate,
        string? MaterialsContext = null,
        string? ProviderName = null,
        int? ConsensusK = null,
        int? MaxRounds = null,
        int? WorkerCount = null,
        int? MaxDepth = null,
        IProgress<ReasoningProgress>? Progress = null);

    public sealed record ConsensusResult(
        bool Ok,
        bool Blocked,
        string Workflow,
        SraDagMutation? Mutation,
        IReadOnlyList<string> RedFlags,
        string? ArtifactPath,
        string? Error);

    public async Task<ConsensusResult> RunAsync(ConsensusInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ct.ThrowIfCancellationRequested();

        var mode = ResolveMode(input);
        if (!IsMaker(mode))
        {
            // Default path: verifier-quorum (or unknown -> treated as verifier-quorum).
            return await RunVerifierQuorumAsync(input, ct);
        }

        var ws = _workspace.EnsureSessionWorkspace(input.SessionId);
        Directory.CreateDirectory(Path.Combine(ws.ArtifactsDir, "dag", "consensus"));

        var task = BuildTaskPrompt(input);
        var options = new ReasoningOptions
        {
            ProviderName = input.ProviderName,
            CognitiveWorkflow = MakerWorkflow,
            CognitiveConsensusK = input.ConsensusK,
            CognitiveMaxRounds = input.MaxRounds,
            CognitiveWorkerCount = input.WorkerCount,
            CognitiveMaxDepth = input.MaxDepth,
            Context = new Dictionary<string, string>
            {
                ["session_id"] = ws.SessionId,
                ["run_id"] = input.RunId ?? string.Empty,
                ["purpose"] = "vibe_dag_consensus"
            }
        };

        ReasoningResult rr;
        try
        {
            rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
        }
        catch (Exception ex)
        {
            return await FailMakerAsync(
                ws,
                input,
                rr: null,
                extractedJson: null,
                parsed: null,
                redFlags: [ErrorCognitiveExecuteException],
                error: ex.Message,
                ct);
        }

        if (!rr.Success || string.IsNullOrWhiteSpace(rr.Content))
        {
            var err = rr.Error ?? "maker failed";
            return await FailMakerAsync(
                ws,
                input,
                rr,
                extractedJson: null,
                parsed: null,
                redFlags: [ErrorMakerFailed],
                error: err,
                ct);
        }

        var raw = rr.Content!;
        if (!TryExtractJson(raw, out var json))
        {
            return await FailMakerAsync(
                ws,
                input,
                rr,
                extractedJson: null,
                parsed: null,
                redFlags: [ErrorJsonParseFailed],
                error: "failed to extract json from maker output",
                ct);
        }

        DagMutationJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DagMutationJson>(json!, Json);
        }
        catch (Exception ex)
        {
            return await FailMakerAsync(
                ws,
                input,
                rr,
                extractedJson: json,
                parsed: null,
                redFlags: [ErrorJsonDeserializeFailed],
                error: ex.Message,
                ct);
        }

        var redFlags = (parsed?.RedFlags ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (redFlags.Count > 0)
        {
            return await FailMakerAsync(
                ws,
                input,
                rr,
                extractedJson: json,
                parsed,
                redFlags,
                error: ErrorBlockedByRedFlags,
                ct);
        }

        var mutation = BuildMutation(ws.SessionId, input.Candidate, parsed);
        if (mutation.UpsertNodes.Count == 0 && mutation.UpsertEdges.Count == 0)
        {
            return await FailMakerAsync(
                ws,
                input,
                rr,
                extractedJson: json,
                parsed,
                redFlags: [ErrorEmptyMutation],
                error: "empty mutation",
                ct);
        }

        var okArtifact = await WriteArtifactAsync(ws, workflow: MakerWorkflow, input, rr, extractedJson: json, parsed, redFlags: [],
            error: null, ct);
        return new ConsensusResult(true, false, MakerWorkflow, mutation, [], okArtifact, null);
    }

    private static string BuildTaskPrompt(ConsensusInput input)
    {
        // Keep prompt compact; maker already decomposes internally.
        var candidateSummary = new
        {
            mutationId = input.Candidate.MutationId,
            authorAgent = input.Candidate.AuthorAgent,
            nodeCount = input.Candidate.UpsertNodes.Count,
            edgeCount = input.Candidate.UpsertEdges.Count,
            nodes = input.Candidate.UpsertNodes.Select(n => new { id = n.Id, type = n.Type.ToString(), label = n.Label }).Take(30).ToList(),
            edges = input.Candidate.UpsertEdges.Select(e => new { from = e.FromId, to = e.ToId, type = e.Type }).Take(60).ToList()
        };

        var currentStats = new
        {
            nodeCount = input.Current.Nodes.Count,
            edgeCount = input.Current.Edges.Count,
            updatedAt = input.Current.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
        };

        return
            """
            You are validating and synthesizing a DAG mutation for a research derivation graph.

            Requirements:
            - Output STRICT JSON ONLY (no markdown, no code fences).
            - Do not invent node ids that are not necessary; prefer reusing existing ids when possible.
            - Edge semantics: dependency -> dependent (from -> to). Use type "depends_on" unless a better typed edge is justified.
            - Keep text bounded: label <= 200 chars; proof <= 2000 chars; long proof should be summarized.

            Validation approach:
            1. FIRST, perform technical validation:
            - Check for ID conflicts, cycles, invalid references
            - Verify nodeCount/edgeCount matches
            - If technical issues found, set redFlags and proved=false

            2. THEN, perform logical validation using proof-based reasoning:
            - proved=true if mutation is internally consistent and stays within provided graph constraints
            - Allow standard inference steps (transitivity, inheritance) without new assumptions
            - Mark proved=false ONLY when you can point to concrete contradiction or physical implausibility
            - IMPORTANT: If mutation requires additional domain knowledge but is CONSISTENT with the graph, still accept as proved=true

            3. Decision logic:
            - If proved=false OR technical issues: redFlags non-empty, nodes/edges empty
            - If proved=true AND no technical issues: populate nodes/edges, redFlags empty
            - Include gapDescription when proved=false to explain the specific violation
            
            Task:
            1) Check the candidate mutation against the current DAG stats.
            2) Normalize node/edge fields and remove obvious duplicates.
            3) If any parsing/consistency issue exists, red-flag with clear reasons.

            CandidateMutationSummary:
            """ + JsonSerializer.Serialize(candidateSummary, Json) + """

            CurrentDagStats:
            """ + JsonSerializer.Serialize(currentStats, Json) + """

            Output JSON schema:
            {
              "mutationId": "string",
              "author": "string",
              "nodes": [{"id":"string","type":"axiom|theorem|assumption|hypothesis|unknown","label":"string","proof":"string","tags":{"k":"v"}}],
              "edges": [{"from":"string","to":"string","type":"depends_on"}],
              "redFlags": ["string"],
              "rejectionReason": "string",
              "validationOutcome": {
                "proved": boolean,
                "confidence": 0~1,
                "gapDescription": "string",
                "acceptedWithCaveats": boolean
              }
            }
            """;
    }

    private static SraDagMutation BuildMutation(string sessionId, SraDagMutation candidate, DagMutationJson? parsed)
    {
        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var id = (parsed?.MutationId ?? string.Empty).Trim();
        if (id.Length == 0)
            id = $"{MakerWorkflow}_{Guid.NewGuid():N}";

        var m = new SraDagMutation
        {
            SessionId = sessionId,
            MutationId = id,
            AuthorAgent = string.IsNullOrWhiteSpace(parsed?.Author) ? MakerWorkflow : parsed!.Author!.Trim(),
            CreatedAt = now
        };

        // Carry original author for audit.
        if (!string.IsNullOrWhiteSpace(candidate.AuthorAgent))
            m.Labels["candidate_author"] = candidate.AuthorAgent.Trim();
        if (!string.IsNullOrWhiteSpace(candidate.MutationId))
            m.Labels["candidate_mutation_id"] = candidate.MutationId.Trim();

        if (parsed?.Nodes != null)
        {
            foreach (var n in parsed.Nodes)
            {
                var nid = (n?.Id ?? string.Empty).Trim();
                if (nid.Length == 0) continue;

                var label = Bound((n!.Label ?? string.Empty).Trim(), 200);
                var proof = Bound((n.Proof ?? string.Empty).Trim(), 2000);

                var node = new SraDagNode
                {
                    Id = nid,
                    Type = ParseNodeType(n.Type),
                    Label = label,
                    Proof = proof,
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

                m.UpsertNodes.Add(node);
            }
        }

        if (parsed?.Edges != null)
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

        return m;
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

    private static string Bound(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s[..max];
    }

    private async Task<string> WriteArtifactAsync(
        WorkspacePaths ws,
        string workflow,
        ConsensusInput input,
        ReasoningResult? rr,
        string? extractedJson,
        DagMutationJson? parsed,
        IReadOnlyList<string> redFlags,
        string? error,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dir = Path.Combine(ws.ArtifactsDir, "dag", "consensus");
        Directory.CreateDirectory(dir);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var name = $"{stamp}_{Guid.NewGuid():N}.json";
        var path = Path.Combine(dir, name);

        var raw = rr?.Content ?? string.Empty;
        raw = raw.Replace("\r", "").Trim();
        if (raw.Length > MaxRawChars)
            raw = raw[..MaxRawChars];

        var obj = new
        {
            sessionId = ws.SessionId,
            runId = input.RunId,
            workflow = workflow,
            status = error == null && redFlags.Count == 0 ? "accepted" : "blocked",
            error,
            redFlags,
            stats = rr == null
                ? null
                : new
                {
                    success = rr.Success,
                    durationMs = (long)rr.Duration.TotalMilliseconds,
                    llmCalls = rr.TotalLlmCalls,
                    promptTokens = rr.PromptTokens,
                    completionTokens = rr.CompletionTokens,
                    totalTokens = rr.TotalTokens
                },
            candidate = new
            {
                mutationId = input.Candidate.MutationId,
                authorAgent = input.Candidate.AuthorAgent,
                nodeCount = input.Candidate.UpsertNodes.Count,
                edgeCount = input.Candidate.UpsertEdges.Count
            },
            extractedJson,
            parsed,
            rawOutput = raw
        };

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions(Json) { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);

        return Path.GetRelativePath(ws.SessionRoot, path).Replace('\\', '/').Trim('/');
    }

    private async Task<ConsensusResult> FailMakerAsync(
        WorkspacePaths ws,
        ConsensusInput input,
        ReasoningResult? rr,
        string? extractedJson,
        DagMutationJson? parsed,
        IReadOnlyList<string> redFlags,
        string error,
        CancellationToken ct)
    {
        var artifact = await WriteArtifactAsync(ws, MakerWorkflow, input, rr, extractedJson, parsed, redFlags, error, ct);
        return new ConsensusResult(false, true, MakerWorkflow, null, redFlags, artifact, error);
    }

    private string ResolveMode(ConsensusInput input)
    {
        // Allow workflow hint in candidate labels (rare override).
        if (input?.Candidate != null &&
            input.Candidate.Labels != null &&
            input.Candidate.Labels.TryGetValue("workflow", out var w) &&
            !string.IsNullOrWhiteSpace(w))
        {
            return w.Trim();
        }

        var mode = (_configuration.GetValue<string?>("Vibe:DagConsensus:Mode") ?? string.Empty).Trim();
        return mode.Length == 0 ? "verifier-quorum" : mode;
    }

    private static bool IsMaker(string mode)
    {
        var s = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return s == MakerWorkflow;
    }

    // Best-effort JSON extraction (reused pattern from tools).
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

    private sealed class DagMutationJson
    {
        public string? MutationId { get; init; }
        public string? Author { get; init; }
        public List<NodeJson>? Nodes { get; init; }
        public List<EdgeJson>? Edges { get; init; }
        public List<string>? RedFlags { get; init; }
    }

    private sealed class NodeJson
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Label { get; init; }
        public string? Proof { get; init; }
        public Dictionary<string, string?>? Tags { get; init; }
    }

    private sealed class EdgeJson
    {
        public string? From { get; init; }
        public string? To { get; init; }
        public string? Type { get; init; }
    }
}


