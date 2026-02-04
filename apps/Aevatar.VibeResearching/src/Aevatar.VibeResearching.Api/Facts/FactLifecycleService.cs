using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Facts;

// ============================================================
//  FactLifecycleService (facts_proposed -> decisions -> DAG facts)
//
//  MVP policy:
//  - Hard verification wins: any FactVerification.result == true => PROMOTE.
//  - Soft consensus: approvals >= RequiredApprovals => PROMOTE.
//  - Soft reject: rejects >= RequiredRejects => REJECT.
//
//  Notes:
//  - All records are Protobuf-defined and persisted as Protobuf-JSON files.
//  - Facts are promoted into DAG knowledge nodes (shared knowledge base).
//  - Proposed facts remain session-scoped under `workspace/sessions/{id}/facts_proposed/`.
// ============================================================

public sealed class FactLifecycleService
{
    private static readonly Regex SafeId = new(@"^[a-zA-Z0-9_-]{6,128}$", RegexOptions.Compiled);

    // MVP thresholds (can be made configurable later).
    private const int RequiredApprovals = 2;
    private const int RequiredRejects = 2;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly WorkspaceService _workspace;
    private readonly DagStore _dag;
    private readonly ILogger<FactLifecycleService> _logger;

    public FactLifecycleService(WorkspaceService workspace, DagStore dag, ILogger<FactLifecycleService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FactProposal> CreateProposalAsync(
        string sessionId,
        string title,
        string content,
        IEnumerable<string>? evidencePaths,
        string proposedBy,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        title = (title ?? string.Empty).Trim();
        content = (content ?? string.Empty).Replace("\r", "").Trim();
        proposedBy = (proposedBy ?? string.Empty).Trim();
        if (proposedBy.Length == 0) proposedBy = "unknown";

        if (content.Length == 0)
            throw new ArgumentException("content is required", nameof(content));

        var factId = GenerateFactId(title);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var proposal = new FactProposal
        {
            SessionId = ws.SessionId,
            FactId = factId,
            Title = title,
            Content = content,
            CreatedAt = now,
            ProposedBy = proposedBy
        };

        if (evidencePaths != null)
        {
            foreach (var p in evidencePaths)
            {
                var t = (p ?? string.Empty).Replace('\\', '/').Trim();
                if (t.Length == 0) continue;
                proposal.EvidencePaths.Add(t);
            }
        }

        Directory.CreateDirectory(ws.FactsProposedDir);
        var path = Path.Combine(ws.FactsProposedDir, $"{factId}.json");
        await WriteProtoJsonAtomicAsync(ws, path, proposal, ct);

        return proposal;
    }

    public async Task RecordVoteAsync(string sessionId, FactVote vote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vote);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        vote.SessionId = ws.SessionId;
        if (vote.CreatedAt == null) vote.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        var factId = NormalizeId(vote.FactId, nameof(vote.FactId));
        var reviewer = NormalizeId(vote.ReviewerId, nameof(vote.ReviewerId));

        var votesDir = Path.Combine(ws.DecisionsDir, "votes", factId);
        Directory.CreateDirectory(votesDir);

        var path = Path.Combine(votesDir, $"{reviewer}.json");
        await WriteProtoJsonAtomicAsync(ws, path, vote, ct);
    }

    public async Task RecordVerificationAsync(string sessionId, FactVerification verification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        verification.SessionId = ws.SessionId;
        if (verification.CreatedAt == null) verification.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        var factId = NormalizeId(verification.FactId, nameof(verification.FactId));
        var verifier = NormalizeId(verification.VerifierId, nameof(verification.VerifierId));

        var verifDir = Path.Combine(ws.DecisionsDir, "verifications", factId);
        Directory.CreateDirectory(verifDir);

        var path = Path.Combine(verifDir, $"{verifier}.json");
        await WriteProtoJsonAtomicAsync(ws, path, verification, ct);
    }

    public async Task<FactDecision?> EvaluateAndPromoteAsync(
        string sessionId,
        string dagId,
        string factId,
        string finalizedBy,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        factId = NormalizeId(factId, nameof(factId));
        finalizedBy = (finalizedBy ?? string.Empty).Trim();
        if (finalizedBy.Length == 0) finalizedBy = "system";

        // If already finalized, return existing decision.
        var finalDir = Path.Combine(ws.DecisionsDir, "final");
        Directory.CreateDirectory(finalDir);
        var finalPath = Path.Combine(finalDir, $"{factId}.json");

        var existingFinal = await TryReadProtoJsonAsync<FactDecision>(finalPath, ct);
        if (existingFinal != null)
            return existingFinal;

        var votes = await ReadVotesAsync(ws, factId, ct);
        var verifs = await ReadVerificationsAsync(ws, factId, ct);

        var (decision, basis, rationale) = Decide(votes, verifs);
        if (decision == FactDecisionValue.Unspecified)
            return null; // no final decision yet

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var final = new FactDecision
        {
            SessionId = ws.SessionId,
            FactId = factId,
            Decision = decision,
            Basis = basis,
            Rationale = rationale,
            FinalizedAt = now,
            FinalizedBy = finalizedBy
        };

        await WriteProtoJsonAtomicAsync(ws, finalPath, final, ct);

        if (decision == FactDecisionValue.Promote)
            await PromoteToDagAsync(ws, dagId, factId, ct);

        return final;
    }

    // ============================================================
    //  Internal
    // ============================================================

    private static (FactDecisionValue decision, string basis, string rationale) Decide(
        IReadOnlyList<FactVote> votes,
        IReadOnlyList<FactVerification> verifs)
    {
        if (verifs.Any(v => v.Result))
        {
            return (FactDecisionValue.Promote, "verification", "hard verification passed");
        }

        var approvals = votes.Count(v => v.Vote == FactVoteValue.Approve);
        var rejects = votes.Count(v => v.Vote == FactVoteValue.Reject);

        if (approvals >= RequiredApprovals)
            return (FactDecisionValue.Promote, "votes", $"approvals={approvals} (threshold={RequiredApprovals})");

        if (rejects >= RequiredRejects)
            return (FactDecisionValue.Reject, "votes", $"rejects={rejects} (threshold={RequiredRejects})");

        return (FactDecisionValue.Unspecified, "", "");
    }

    private async Task PromoteToDagAsync(WorkspacePaths ws, string dagId, string factId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var src = Path.Combine(ws.FactsProposedDir, $"{factId}.json");

        if (!File.Exists(src))
            throw new FileNotFoundException("fact proposal not found", src);

        var proposal = await TryReadProtoJsonAsync<FactProposal>(src, ct);
        if (proposal == null)
            throw new InvalidOperationException("fact proposal parse failed");

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var nodeId = NormalizeId(factId, nameof(factId));
        var label = Bound((proposal.Title ?? string.Empty).Trim(), 200);
        var proof = BuildFactProof(proposal.Content, proposal.EvidencePaths);

        var node = new SraDagNode
        {
            Id = nodeId,
            Type = SraDagNodeType.Axiom,
            Kind = SraDagNodeKind.Knowledge,
            Label = label.Length == 0 ? nodeId : label,
            Proof = Bound(proof, 2000),
            UpdatedAt = now,
            SessionId = ws.SessionId
        };

        if (proposal.Tags != null && proposal.Tags.Count > 0)
        {
            foreach (var kv in proposal.Tags)
            {
                var key = (kv.Key ?? string.Empty).Trim();
                var val = (kv.Value ?? string.Empty).Trim();
                if (key.Length == 0) continue;
                node.Tags[key] = val;
            }
        }

        var mutation = new SraDagMutation
        {
            SessionId = ws.SessionId,
            MutationId = $"fact_promote_{factId}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            AuthorAgent = "fact_lifecycle",
            CreatedAt = now
        };
        mutation.UpsertNodes.Add(node);

        await _dag.ApplyMutationAsync(dagId, mutation, ct);
    }

    private async Task<List<FactVote>> ReadVotesAsync(WorkspacePaths ws, string factId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.Combine(ws.DecisionsDir, "votes", factId);
        if (!Directory.Exists(dir))
            return [];

        var list = new List<FactVote>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var msg = await TryReadProtoJsonAsync<FactVote>(path, ct);
            if (msg != null) list.Add(msg);
        }

        return list;
    }

    private async Task<List<FactVerification>> ReadVerificationsAsync(WorkspacePaths ws, string factId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.Combine(ws.DecisionsDir, "verifications", factId);
        if (!Directory.Exists(dir))
            return [];

        var list = new List<FactVerification>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var msg = await TryReadProtoJsonAsync<FactVerification>(path, ct);
            if (msg != null) list.Add(msg);
        }

        return list;
    }

    private async Task<T?> TryReadProtoJsonAsync<T>(string path, CancellationToken ct)
        where T : class, IMessage<T>, new()
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return Parser.Parse<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read proto-json from {Path}", path);
            return null;
        }
    }

    private static async Task WriteProtoJsonAtomicAsync(WorkspacePaths ws, string path, IMessage message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        var json = Formatter.Format(message);

        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(tmp, path, overwrite: true);
    }

    private static string NormalizeId(string id, string argName)
    {
        id = (id ?? string.Empty).Trim();
        if (!SafeId.IsMatch(id))
            throw new ArgumentException("invalid id format", argName);
        return id;
    }

    private static string GenerateFactId(string title)
    {
        // Prefer deterministic-ish but collision-resistant ids.
        var slug = Slugify(title);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var rand = Guid.NewGuid().ToString("N")[..6];
        var id = $"fact_{stamp}_{rand}_{slug}";
        return NormalizeId(id, nameof(title));
    }

    private static string Slugify(string s)
    {
        s = (s ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return "untitled";

        var sb = new StringBuilder(Math.Min(48, s.Length));
        foreach (var ch in s)
        {
            if (sb.Length >= 48) break;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('_');
        }

        var slug = sb.ToString().Trim('_');
        return slug.Length == 0 ? "untitled" : slug;
    }

    private static string BuildFactProof(string content, IEnumerable<string> evidencePaths)
    {
        var body = (content ?? string.Empty).Replace("\r", "").Trim();
        var evidence = evidencePaths?
            .Select(p => (p ?? string.Empty).Replace('\\', '/').Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList() ?? new List<string>();

        if (evidence.Count == 0) return body;

        var sb = new StringBuilder();
        sb.AppendLine(body);
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        foreach (var p in evidence)
            sb.Append("- ").Append(p).AppendLine();
        return sb.ToString().Trim();
    }

    private static string Bound(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s[..max];
    }
}


