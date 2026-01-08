using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Workspace;
using ScientificResearchAssistant.Contracts.Collab;

namespace ScientificResearchAssistant.Api.Vibe.Dag;

// ============================================================
//  DagStore (File-SSoT)
//
//  Files:
//    artifacts/dag/snapshot.json
//    artifacts/dag/staged/*.json       (no-consensus candidates)
//    artifacts/dag/consensus/*.json    (consensus artifacts/logs)
//
//  Notes:
//  - Snapshot stored as Protobuf-JSON (review friendly).
//  - Writes are atomic (tmp -> move).
//  - Ordering is deterministic for stable diffs.
// ============================================================

public sealed class DagStore
{
    private const int MaxNodesForList = 2000;
    private const int MaxEdgesForList = 8000;
    private const int MaxStagedList = 80;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly WorkspaceService _workspace;
    private readonly ILogger<DagStore> _logger;

    public DagStore(WorkspaceService workspace, ILogger<DagStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetSnapshotPath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return Path.Combine(GetDagDir(ws), "snapshot.json");
    }

    public async Task<SraDagSnapshot> LoadSnapshotAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = GetSnapshotPath(ws.SessionId);

        try
        {
            EnsureDagDirs(ws);
            if (!File.Exists(path))
                return new SraDagSnapshot { SessionId = ws.SessionId, UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow) };

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new SraDagSnapshot { SessionId = ws.SessionId, UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow) };

            var snap = Parser.Parse<SraDagSnapshot>(json);
            if (string.IsNullOrWhiteSpace(snap.SessionId))
                snap.SessionId = ws.SessionId;
            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load dag snapshot (best-effort).");
            return new SraDagSnapshot { SessionId = ws.SessionId, UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
        }
    }

    public async Task<SraDagSnapshot> ApplyMutationAsync(string sessionId, SraDagMutation mutation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        EnsureDagDirs(ws);

        var snap = await LoadSnapshotAsync(ws.SessionId, ct);
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // Index existing nodes/edges
        var nodes = new Dictionary<string, SraDagNode>(StringComparer.Ordinal);
        foreach (var n in snap.Nodes)
        {
            if (n is null) continue;
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            nodes[id] = n;
        }

        var edges = new Dictionary<string, SraDagEdge>(StringComparer.Ordinal);
        foreach (var e in snap.Edges)
        {
            if (e is null) continue;
            var from = (e.FromId ?? string.Empty).Trim();
            var to = (e.ToId ?? string.Empty).Trim();
            var type = NormalizeEdgeType(e.Type);
            if (from.Length == 0 || to.Length == 0) continue;
            edges[$"{from}->{to}:{type}"] = new SraDagEdge { FromId = from, ToId = to, Type = type, UpdatedAt = e.UpdatedAt ?? now };
        }

        // Upsert nodes
        foreach (var n in mutation.UpsertNodes)
        {
            if (n is null) continue;
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;

            if (!nodes.TryGetValue(id, out var existing))
            {
                var created = new SraDagNode
                {
                    Id = id,
                    Type = n.Type,
                    Label = (n.Label ?? string.Empty).Trim(),
                    Proof = (n.Proof ?? string.Empty).Trim(),
                    UpdatedAt = now
                };

                foreach (var kv in n.Tags)
                    created.Tags[kv.Key] = kv.Value;

                nodes[id] = created;
                continue;
            }

            // Merge: do not erase non-empty fields with empty strings.
            var merged = new SraDagNode
            {
                Id = id,
                Type = n.Type != SraDagNodeType.Unspecified ? n.Type : existing.Type,
                Label = string.IsNullOrWhiteSpace(n.Label) ? (existing.Label ?? "") : n.Label.Trim(),
                Proof = string.IsNullOrWhiteSpace(n.Proof) ? (existing.Proof ?? "") : n.Proof.Trim(),
                UpdatedAt = now
            };

            foreach (var kv in existing.Tags)
                merged.Tags[kv.Key] = kv.Value;
            foreach (var kv in n.Tags)
                merged.Tags[kv.Key] = kv.Value;

            nodes[id] = merged;
        }

        // Upsert edges
        foreach (var e in mutation.UpsertEdges)
        {
            if (e is null) continue;
            var from = (e.FromId ?? string.Empty).Trim();
            var to = (e.ToId ?? string.Empty).Trim();
            if (from.Length == 0 || to.Length == 0) continue;
            var type = NormalizeEdgeType(e.Type);

            var key = $"{from}->{to}:{type}";
            edges[key] = new SraDagEdge
            {
                FromId = from,
                ToId = to,
                Type = type,
                UpdatedAt = now
            };
        }

        // Write snapshot deterministically
        var outSnap = new SraDagSnapshot
        {
            SessionId = ws.SessionId,
            UpdatedAt = now
        };

        foreach (var n in nodes.Values.OrderBy(x => x.Type).ThenBy(x => x.Id, StringComparer.Ordinal))
            outSnap.Nodes.Add(n);

        foreach (var e in edges.Values.OrderBy(x => x.FromId, StringComparer.Ordinal)
                     .ThenBy(x => x.ToId, StringComparer.Ordinal)
                     .ThenBy(x => x.Type, StringComparer.Ordinal))
        {
            outSnap.Edges.Add(e);
        }

        await SaveSnapshotAsync(ws, outSnap, ct);
        return outSnap;
    }

    public async Task<string> WriteStagedAsync(string sessionId, SraDagMutation candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        EnsureDagDirs(ws);

        var id = (candidate.MutationId ?? string.Empty).Trim();
        if (id.Length == 0)
            id = Guid.NewGuid().ToString("N");

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var agent = SanitizeToken(candidate.AuthorAgent ?? "unknown");
        var file = $"{stamp}_{SanitizeToken(id)}_{agent}.json";

        var path = Path.Combine(GetStagedDir(ws), file);
        var json = Formatter.Format(candidate);
        await WriteFileAtomicAsync(ws, path, json, ct);
        return NormalizeRelative(Path.GetRelativePath(ws.SessionRoot, path));
    }

    public async Task<List<object>> ListStagedAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        EnsureDagDirs(ws);

        var dir = GetStagedDir(ws);
        if (!Directory.Exists(dir))
            return [];

        // List newest files only; do not parse contents here (bounded + fast).
        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Take(MaxStagedList)
            .ToList();

        return files.Select(fi => (object)new
        {
            file = NormalizeRelative(Path.GetRelativePath(ws.SessionRoot, fi.FullName)),
            sizeBytes = fi.Length,
            updatedAt = fi.LastWriteTimeUtc.ToString("O")
        }).ToList();
    }

    public async Task<object> GetSnapshotForListAsync(string sessionId, CancellationToken ct)
    {
        var snap = await LoadSnapshotAsync(sessionId, ct);

        var nodes = snap.Nodes.Take(MaxNodesForList).Select(n => new
        {
            id = n.Id,
            type = n.Type.ToString(),
            label = n.Label,
            proof = n.Proof,
            updatedAt = n.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
        }).ToList();

        var edges = snap.Edges.Take(MaxEdgesForList).Select(e => new
        {
            fromId = e.FromId,
            toId = e.ToId,
            type = e.Type,
            updatedAt = e.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
        }).ToList();

        return new
        {
            sessionId = snap.SessionId,
            updatedAt = snap.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
            nodes,
            edges,
            truncated = new { nodes = snap.Nodes.Count > MaxNodesForList, edges = snap.Edges.Count > MaxEdgesForList }
        };
    }

    // ============================================================
    //  Internal helpers
    // ============================================================

    private static string GetDagDir(WorkspacePaths ws) => Path.Combine(ws.ArtifactsDir, "dag");
    private static string GetStagedDir(WorkspacePaths ws) => Path.Combine(GetDagDir(ws), "staged");
    private static string GetConsensusDir(WorkspacePaths ws) => Path.Combine(GetDagDir(ws), "consensus");

    private static void EnsureDagDirs(WorkspacePaths ws)
    {
        Directory.CreateDirectory(GetDagDir(ws));
        Directory.CreateDirectory(GetStagedDir(ws));
        Directory.CreateDirectory(GetConsensusDir(ws));
    }

    private async Task SaveSnapshotAsync(WorkspacePaths ws, SraDagSnapshot snapshot, CancellationToken ct)
    {
        var path = GetSnapshotPath(ws.SessionId);
        var json = Formatter.Format(snapshot);
        await WriteFileAtomicAsync(ws, path, json, ct);
    }

    private static async Task WriteFileAtomicAsync(WorkspacePaths ws, string targetPath, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ws.TmpDir);

        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content ?? string.Empty, Encoding.UTF8, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(tmp, targetPath, overwrite: true);
    }

    private static string NormalizeEdgeType(string? s)
    {
        var t = (s ?? string.Empty).Trim();
        return t.Length == 0 ? "depends_on" : t;
    }

    private static string SanitizeToken(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return "x";
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    private static string NormalizeRelative(string s) => (s ?? string.Empty).Replace('\\', '/').Trim('/');
}


