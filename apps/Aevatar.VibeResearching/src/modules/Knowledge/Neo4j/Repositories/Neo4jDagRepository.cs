using System.Collections.Concurrent;
using System.Text;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Core.Secrets;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Knowledge;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Orchestration;

namespace Aevatar.VibeResearching.Knowledge.Neo4j;

/// <summary>
/// Neo4j-backed DAG repository implementation.
/// Manages persistent knowledge graph with consensus and staging support.
/// </summary>
public sealed class Neo4jDagRepository : IDagRepository
{
    private const int MaxNodesForList = 2000;
    private const int MaxEdgesForList = 8000;
    private const int MaxStagedList = 80;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly IWorkspaceService _workspace;
    private readonly IKnowledgeGraphClientFactory _graphFactory;
    private readonly IAevatarUserSecretsStore _secrets;
    private readonly ILogger<Neo4jDagRepository> _logger;
    private readonly ConcurrentDictionary<string, byte> _hydrated = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public Neo4jDagRepository(
        IWorkspaceService workspace,
        IKnowledgeGraphClientFactory graphFactory,
        IAevatarUserSecretsStore secrets,
        ILogger<Neo4jDagRepository> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _graphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string? TryGetLocalDagOwnerPubKey()
    {
        try
        {
            if (_secrets.TryGet(global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DagOwnerPublicKeySecretsKey, out var v))
            {
                var t = (v ?? string.Empty).Trim();
                return t.Length == 0 ? null : t;
            }
        }
        catch
        {
            // best-effort only
        }
        return null;
    }

    public string GetSnapshotPath(string dagId)
    {
        var ws = _workspace.EnsureDagWorkspace(dagId);
        return Path.Combine(GetDagDir(ws), global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DagSnapshotFile);
    }

    public async Task<SraDagSnapshot> LoadSnapshotAsync(string dagId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureDagWorkspace(dagId);

        try
        {
            EnsureDagDirs(ws);
            await EnsureHydratedAsync(ws, ct);
            return await BuildSnapshotFromGraphAsync(ws.DagId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load dag snapshot for dagId={DagId}.", ws.DagId);
            return new SraDagSnapshot { SessionId = ws.DagId, UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
        }
    }

    public async Task<SraDagSnapshot> LoadMergedSnapshotAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Load global knowledge nodes (all sessions).
        var snap = await LoadSnapshotAsync("global", ct);
        snap.SessionId = sessionId;

        // 2. Merge current session's plan nodes.
        try
        {
            var sessionClient = _graphFactory.CreateClient(sessionId);
            var sessionGraph = await sessionClient.GetGraphSnapshotAsync(ct);

            foreach (var pn in sessionGraph.PlanNodes)
            {
                if (pn == null) continue;
                var id = (pn.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;

                snap.Nodes.Add(new SraDagNode
                {
                    Id = id,
                    Type = SraDagNodeType.Assumption,
                    Kind = SraDagNodeKind.Plan,
                    Owner = pn.Owner ?? "",
                    Label = Bound((pn.CoreDescription ?? string.Empty).Trim(), 200),
                    Proof = Bound((pn.DetailedDescription ?? "").Trim(), 1200),
                    SessionId = sessionId,
                    PlanStatus = MapPlanNodeStatus(pn.Status),
                    UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(pn.UpdatedAt.UtcDateTime, DateTimeKind.Utc))
                });
            }

            // Also merge session-specific edges for plan nodes.
            foreach (var e in sessionGraph.Edges)
            {
                if (e == null) continue;
                var dependent = (e.FromId ?? string.Empty).Trim();
                var dep = (e.ToId ?? string.Empty).Trim();
                if (dependent.Length == 0 || dep.Length == 0) continue;

                snap.Edges.Add(new SraDagEdge
                {
                    FromId = dep,
                    ToId = dependent,
                    Type = KnowledgeConsts.DefaultEdgeType,
                    UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(e.CreatedAt.UtcDateTime, DateTimeKind.Utc))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to merge session plan nodes for sessionId={SessionId} (best-effort).", sessionId);
        }

        return snap;
    }

    public async Task<SraDagSnapshot> ApplyMutationAsync(string dagId, SraDagMutation mutation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureDagWorkspace(dagId);
        EnsureDagDirs(ws);

        var gate = _locks.GetOrAdd(ws.DagId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await EnsureHydratedAsync(ws, ct);

            // Use the actual session ID from mutation for writing nodes (preserves cross-session identity).
            var writeSessionId = string.IsNullOrWhiteSpace(mutation.SessionId) ? ws.DagId : mutation.SessionId;
            var client = _graphFactory.CreateClient(writeSessionId);
            var localOwner = TryGetLocalDagOwnerPubKey();

            // Upsert nodes
            foreach (var n in mutation.UpsertNodes)
            {
                ct.ThrowIfCancellationRequested();
                if (n is null) continue;
                var id = (n.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;

                var label = (n.Label ?? string.Empty).Trim();
                var proof = (n.Proof ?? string.Empty).Trim();
                var baseDetail = string.IsNullOrWhiteSpace(label) ? proof : label;
                var detail = AppendTags(baseDetail, n.Tags);

                try
                {
                    var isMilestoneMutation = mutation.Labels.TryGetValue("planKind", out var planKind) &&
                                              string.Equals(planKind, "milestone", StringComparison.OrdinalIgnoreCase);

                    if (n.Kind == SraDagNodeKind.Plan && isMilestoneMutation)
                    {
                        var existing = await client.GetNodeAsync(id, ct);
                        if (existing == null)
                        {
                            await client.CreatePlanNodeAsync(
                                nodeId: id,
                                coreDescription: string.IsNullOrWhiteSpace(label) ? id : label,
                                detailedDescription: string.IsNullOrWhiteSpace(detail) ? id : detail,
                                methodology: string.IsNullOrWhiteSpace(proof) ? null : proof,
                                cancellationToken: ct);
                        }
                        else
                        {
                            _logger.LogDebug("PlanNode {NodeId} already exists, skipping upsert.", id);
                        }
                    }
                    else if (n.Kind == SraDagNodeKind.Plan)
                    {
                        _logger.LogWarning("Rejected Plan node creation for {NodeId} - only milestone mutations can create Plan nodes.", id);
                    }
                    else
                    {
                        await client.UpsertNodeAsync(
                            nodeId: id,
                            nodeType: MapDagNodeType(n.Type),
                            owner: string.IsNullOrWhiteSpace(n.Owner) ? localOwner : n.Owner.Trim(),
                            coreDescription: label,
                            detailedDescription: detail,
                            proof: string.IsNullOrWhiteSpace(proof) ? null : proof,
                            resourceFolderPath: null,
                            cancellationToken: ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to upsert dag node {NodeId} (best-effort).", id);
                }
            }

            // Ensure referenced nodes exist (placeholder nodes)
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in mutation.UpsertEdges)
            {
                if (e is null) continue;
                var from = (e.FromId ?? string.Empty).Trim();
                var to = (e.ToId ?? string.Empty).Trim();
                if (from.Length == 0 || to.Length == 0) continue;
                referenced.Add(from);
                referenced.Add(to);
            }

            foreach (var id in referenced)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var existing = await client.GetNodeAsync(id, ct);
                    if (existing != null) continue;

                    if (id.StartsWith("plan_", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping placeholder creation for missing plan node {NodeId}.", id);
                        continue;
                    }
                    else
                    {
                        await client.UpsertNodeAsync(
                            nodeId: id,
                            nodeType: KnowledgeNodeType.Generic,
                            owner: localOwner,
                            coreDescription: id,
                            detailedDescription: id,
                            proof: null,
                            resourceFolderPath: null,
                            cancellationToken: ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to ensure placeholder dag node {NodeId} (best-effort).", id);
                }
            }

            // Apply edges
            var depsByDependent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var e in mutation.UpsertEdges)
            {
                ct.ThrowIfCancellationRequested();
                if (e is null) continue;
                var dep = (e.FromId ?? string.Empty).Trim();
                var dependent = (e.ToId ?? string.Empty).Trim();
                if (dep.Length == 0 || dependent.Length == 0) continue;

                if (!depsByDependent.TryGetValue(dependent, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    depsByDependent[dependent] = set;
                }
                set.Add(dep);
            }

            foreach (var (dependent, deps) in depsByDependent)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await client.AddDependenciesAsync(dependent, deps, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to add dependencies for {NodeId} (best-effort).", dependent);
                }
            }

            var outSnap = await BuildSnapshotFromGraphAsync(ws.DagId, ct);
            try
            {
                await SaveSnapshotAsync(ws, outSnap, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to save dag snapshot (best-effort).");
            }

            return outSnap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply dag mutation for dagId={DagId}.", ws.DagId);
            return new SraDagSnapshot { SessionId = ws.DagId, UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
        }
        finally
        {
            try { gate.Release(); } catch { /* ignore */ }
        }
    }

    public async Task<string> WriteStagedAsync(string dagId, SraDagMutation candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureDagWorkspace(dagId);
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
        return NormalizeRelative(Path.GetRelativePath(ws.DagRoot, path));
    }

    public async Task<List<object>> ListStagedAsync(string dagId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureDagWorkspace(dagId);
        EnsureDagDirs(ws);

        var dir = GetStagedDir(ws);
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Take(MaxStagedList)
            .ToList();

        return files.Select(fi => (object)new
        {
            file = NormalizeRelative(Path.GetRelativePath(ws.DagRoot, fi.FullName)),
            sizeBytes = fi.Length,
            updatedAt = fi.LastWriteTimeUtc.ToString("O")
        }).ToList();
    }

    public async Task<object> GetSnapshotForListAsync(string dagId, CancellationToken ct, string? currentSessionId = null)
    {
        var snap = await LoadSnapshotAsync(dagId, ct);

        // For global DAG, also include current session's PlanNodes
        if (dagId == "global" && !string.IsNullOrWhiteSpace(currentSessionId))
        {
            try
            {
                var sessionClient = _graphFactory.CreateClient(currentSessionId);
                var sessionGraph = await sessionClient.GetGraphSnapshotAsync(ct);
                var planNodes = sessionGraph.PlanNodes;

                if (planNodes.Count > 0)
                {
                    var now = Timestamp.FromDateTime(DateTime.UtcNow);
                    foreach (var pn in planNodes)
                    {
                        if (pn == null) continue;
                        var id = (pn.Id ?? string.Empty).Trim();
                        if (id.Length == 0) continue;

                        var label = Bound((pn.CoreDescription ?? string.Empty).Trim(), 200);
                        var proof = Bound((pn.DetailedDescription ?? "").Trim(), 1200);
                        var ts = pn.UpdatedAt;

                        snap.Nodes.Add(new SraDagNode
                        {
                            Id = id,
                            Type = SraDagNodeType.Assumption,
                            Kind = SraDagNodeKind.Plan,
                            Owner = pn.Owner ?? "",
                            Label = label,
                            Proof = proof,
                            SessionId = currentSessionId,
                            PlanStatus = MapPlanNodeStatus(pn.Status),
                            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
                        });
                    }
                }
            }
            catch
            {
                // best-effort: session plan nodes are optional enhancement
            }
        }

        var nodes = snap.Nodes.Take(MaxNodesForList).Select(n => new
        {
            id = n.Id,
            type = n.Type.ToString(),
            kind = n.Kind.ToString(),
            owner = n.Owner ?? "",
            label = n.Label,
            proof = n.Proof,
            attestationsCount = n.Attestations.Count,
            attestations = n.Attestations
                .Take(20)
                .Select(a => new { pubkey = a.Pubkey ?? "", signature = a.Signature ?? "" })
                .ToList(),
            updatedAt = n.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
            sessionId = n.SessionId ?? "",
            planStatus = n.Kind == SraDagNodeKind.Plan ? n.PlanStatus.ToString() : null
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

    // Internal helpers
    private static string GetDagDir(DagWorkspacePaths ws) => Path.Combine(ws.ArtifactsDir, global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DagArtifactsDir);
    private static string GetStagedDir(DagWorkspacePaths ws) => Path.Combine(GetDagDir(ws), global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DagStagedDir);
    private static string GetConsensusDir(DagWorkspacePaths ws) => Path.Combine(GetDagDir(ws), global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DagConsensusDir);

    private static void EnsureDagDirs(DagWorkspacePaths ws)
    {
        Directory.CreateDirectory(GetDagDir(ws));
        Directory.CreateDirectory(GetStagedDir(ws));
        Directory.CreateDirectory(GetConsensusDir(ws));
    }

    private async Task EnsureHydratedAsync(DagWorkspacePaths ws, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_hydrated.TryAdd(ws.DagId, 1))
        {
            return;
        }

        try
        {
            var client = _graphFactory.CreateClient(ws.DagId);
            if (ws.DagId == "global")
            {
                var allNodes = await client.GetAllKnowledgeNodesGlobalAsync(ct);
                if (allNodes.Count > 0)
                {
                    return;
                }
            }
            else
            {
                var cur = await client.GetGraphSnapshotAsync(ct);
                if (cur.NodeCount > 0 || cur.EdgeCount > 0)
                {
                    return;
                }
            }

            var path = GetSnapshotPath(ws.DagId);
            if (!File.Exists(path))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            SraDagSnapshot snap;
            try
            {
                snap = Parser.Parse<SraDagSnapshot>(json);
            }
            catch
            {
                return;
            }

            await ImportSnapshotToGraphAsync(client, snap, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hydrate knowledge graph from snapshot.json for dagId={DagId}.", ws.DagId);
        }
    }

    private async Task ImportSnapshotToGraphAsync(IKnowledgeGraphClient client, SraDagSnapshot snapshot, CancellationToken ct)
    {
        snapshot ??= new SraDagSnapshot();
        var localOwner = TryGetLocalDagOwnerPubKey();

        // Nodes
        foreach (var n in snapshot.Nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (n is null) continue;
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;

            var label = (n.Label ?? string.Empty).Trim();
            var proof = (n.Proof ?? string.Empty).Trim();
            var baseDetail = string.IsNullOrWhiteSpace(label) ? proof : label;
            var detail = AppendTags(baseDetail, n.Tags);

            try
            {
                await client.UpsertNodeAsync(
                    nodeId: id,
                    nodeType: MapDagNodeType(n.Type),
                    owner: string.IsNullOrWhiteSpace(n.Owner) ? localOwner : n.Owner.Trim(),
                    coreDescription: label,
                    detailedDescription: detail,
                    proof: string.IsNullOrWhiteSpace(proof) ? null : proof,
                    resourceFolderPath: null,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to hydrate dag node {NodeId} (best-effort).", id);
            }
        }

        // Placeholders for referenced nodes
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in snapshot.Edges)
        {
            if (e is null) continue;
            var from = (e.FromId ?? string.Empty).Trim();
            var to = (e.ToId ?? string.Empty).Trim();
            if (from.Length == 0 || to.Length == 0) continue;
            referenced.Add(from);
            referenced.Add(to);
        }

        foreach (var id in referenced)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var existing = await client.GetNodeAsync(id, ct);
                if (existing != null) continue;

                await client.UpsertNodeAsync(
                    nodeId: id,
                    nodeType: KnowledgeNodeType.Generic,
                    owner: localOwner,
                    coreDescription: id,
                    detailedDescription: id,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to ensure placeholder dag node {NodeId} during hydration (best-effort).", id);
            }
        }

        // Edges
        var depsByDependent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in snapshot.Edges)
        {
            ct.ThrowIfCancellationRequested();
            if (e is null) continue;
            var dep = (e.FromId ?? string.Empty).Trim();
            var dependent = (e.ToId ?? string.Empty).Trim();
            if (dep.Length == 0 || dependent.Length == 0) continue;

            if (!depsByDependent.TryGetValue(dependent, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                depsByDependent[dependent] = set;
            }
            set.Add(dep);
        }

        foreach (var (dependent, deps) in depsByDependent)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await client.AddDependenciesAsync(dependent, deps, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to add dependencies for {NodeId} during hydration (best-effort).", dependent);
            }
        }
    }

    private async Task<SraDagSnapshot> BuildSnapshotFromGraphAsync(string dagId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var client = _graphFactory.CreateClient(dagId);

        IReadOnlyList<IGraphNode> allNodes;
        IReadOnlyList<KnowledgeEdge> allEdges;

        if (dagId == "global")
        {
            var knowledgeNodes = await client.GetAllKnowledgeNodesGlobalAsync(ct);
            var edgesWithSession = await client.GetAllEdgesGlobalAsync(ct);
            allNodes = knowledgeNodes.Cast<IGraphNode>().ToList();
            allEdges = edgesWithSession.Select(e => e.Edge).ToList();
        }
        else
        {
            var graph = await client.GetGraphSnapshotAsync(ct);
            allNodes = graph.AllNodes.ToList();
            allEdges = graph.Edges.Cast<KnowledgeEdge>().ToList();
        }

        var nodeList = new List<SraDagNode>(capacity: Math.Max(0, allNodes.Count));
        var edgeList = new List<SraDagEdge>(capacity: Math.Max(0, allEdges.Count));

        var any = false;
        var maxTs = DateTimeOffset.MinValue;

        foreach (var n in allNodes)
        {
            ct.ThrowIfCancellationRequested();
            if (n == null) continue;
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;

            var label = Bound((n.CoreDescription ?? string.Empty).Trim(), 200);

            if (n is KnowledgeNode kn)
            {
                var ts = kn.CreatedAt;
                any = true;
                if (ts > maxTs) maxTs = ts;

                var proof = string.IsNullOrWhiteSpace(kn.Proof) ? (kn.DetailedDescription ?? "") : kn.Proof!;
                proof = Bound(proof.Trim(), 1200);

                nodeList.Add(new SraDagNode
                {
                    Id = id,
                    Type = MapKnowledgeNodeType(kn.NodeType),
                    Kind = SraDagNodeKind.Knowledge,
                    Owner = kn.Owner ?? "",
                    Label = label,
                    Proof = proof,
                    SessionId = kn.SessionId ?? "",
                    UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
                });

                try
                {
                    var atts = (kn.Attestations ?? Array.Empty<KnowledgeAttestation>())
                        .Where(a => a != null)
                        .Select(a => new
                        {
                            pub = (a.PubKey ?? string.Empty).Trim(),
                            sig = (a.Signature ?? string.Empty).Trim()
                        })
                        .Where(x => x.pub.Length > 0 && x.sig.Length > 0)
                        .OrderBy(x => x.pub, StringComparer.Ordinal)
                        .Take(50)
                        .ToList();

                    foreach (var a in atts)
                    {
                        nodeList[^1].Attestations.Add(new SraDagAttestation
                        {
                            Pubkey = a.pub,
                            Signature = a.sig
                        });
                    }
                }
                catch
                {
                    // best-effort only
                }
            }
            else if (n is PlanNode pn)
            {
                var ts = pn.UpdatedAt;
                any = true;
                if (ts > maxTs) maxTs = ts;

                var proof = Bound((pn.DetailedDescription ?? "").Trim(), 1200);

                nodeList.Add(new SraDagNode
                {
                    Id = id,
                    Type = SraDagNodeType.Assumption,
                    Kind = SraDagNodeKind.Plan,
                    Owner = pn.Owner ?? "",
                    Label = label,
                    Proof = proof,
                    SessionId = pn.SessionId ?? "",
                    PlanStatus = MapPlanNodeStatus(pn.Status),
                    UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
                });
            }
        }

        foreach (var e in allEdges)
        {
            ct.ThrowIfCancellationRequested();
            if (e == null) continue;
            var dependent = (e.FromId ?? string.Empty).Trim();
            var dep = (e.ToId ?? string.Empty).Trim();
            if (dependent.Length == 0 || dep.Length == 0) continue;

            var ts = e.CreatedAt;
            any = true;
            if (ts > maxTs) maxTs = ts;

            edgeList.Add(new SraDagEdge
            {
                FromId = dep,
                ToId = dependent,
                Type = global::Aevatar.VibeResearching.Knowledge.KnowledgeConsts.DefaultEdgeType,
                UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ts.UtcDateTime, DateTimeKind.Utc))
            });
        }

        var updatedAt = any ? maxTs : DateTimeOffset.UtcNow;
        var outSnap = new SraDagSnapshot
        {
            SessionId = dagId,
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(updatedAt.UtcDateTime, DateTimeKind.Utc))
        };

        foreach (var n in nodeList.OrderBy(x => x.Type).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            outSnap.Nodes.Add(n);
        }

        foreach (var e in edgeList
                     .OrderBy(x => x.FromId, StringComparer.Ordinal)
                     .ThenBy(x => x.ToId, StringComparer.Ordinal)
                     .ThenBy(x => x.Type, StringComparer.Ordinal))
        {
            outSnap.Edges.Add(e);
        }

        return outSnap;
    }

    private static KnowledgeNodeType MapDagNodeType(SraDagNodeType t) =>
        t switch
        {
            SraDagNodeType.Axiom => KnowledgeNodeType.MathAxiom,
            SraDagNodeType.Theorem => KnowledgeNodeType.MathTheorem,
            SraDagNodeType.Hypothesis => KnowledgeNodeType.ResearchHypothesis,
            SraDagNodeType.Assumption => KnowledgeNodeType.Note,
            _ => KnowledgeNodeType.Generic
        };

    private static SraDagPlanStatus MapPlanNodeStatus(PlanNodeStatus s) =>
        s switch
        {
            PlanNodeStatus.Pending => SraDagPlanStatus.Pending,
            PlanNodeStatus.Active => SraDagPlanStatus.Active,
            PlanNodeStatus.Completed => SraDagPlanStatus.Completed,
            PlanNodeStatus.Cancelled => SraDagPlanStatus.Cancelled,
            _ => SraDagPlanStatus.Unspecified
        };

    private static SraDagNodeType MapKnowledgeNodeType(KnowledgeNodeType t) =>
        t switch
        {
            KnowledgeNodeType.MathAxiom => SraDagNodeType.Axiom,
            KnowledgeNodeType.Reference => SraDagNodeType.Axiom,
            KnowledgeNodeType.ResearchPaper => SraDagNodeType.Axiom,
            KnowledgeNodeType.ResearchDataset => SraDagNodeType.Axiom,
            KnowledgeNodeType.ResearchAnalysis => SraDagNodeType.Axiom,
            KnowledgeNodeType.Summary => SraDagNodeType.Axiom,
            KnowledgeNodeType.ResearchHypothesis => SraDagNodeType.Hypothesis,
            KnowledgeNodeType.Note => SraDagNodeType.Assumption,
            KnowledgeNodeType.MathTheorem
                or KnowledgeNodeType.MathLemma
                or KnowledgeNodeType.MathCorollary
                or KnowledgeNodeType.MathProof
                or KnowledgeNodeType.MathDefinition
                or KnowledgeNodeType.PhysicsLaw
                or KnowledgeNodeType.PhysicsTheory
                or KnowledgeNodeType.PhysicsExperiment
                or KnowledgeNodeType.BiologyExperiment
                or KnowledgeNodeType.BiologyProcess
                or KnowledgeNodeType.BiologyStructure
                or KnowledgeNodeType.ChemistryExperiment
                or KnowledgeNodeType.ChemistryReaction
                or KnowledgeNodeType.CsAlgorithm
                or KnowledgeNodeType.CsDataStructure
                or KnowledgeNodeType.CsDesignPattern => SraDagNodeType.Theorem,
            _ => SraDagNodeType.Unknown
        };

    private static string AppendTags(string baseText, MapField<string, string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return (baseText ?? string.Empty).Trim();
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(baseText))
        {
            sb.AppendLine(baseText.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("Tags:");
        foreach (var kv in tags.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            sb.Append("- ").Append(kv.Key.Trim()).Append(": ").Append((kv.Value ?? string.Empty).Trim()).AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string Bound(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s[..max];
    }

    private async Task SaveSnapshotAsync(DagWorkspacePaths ws, SraDagSnapshot snapshot, CancellationToken ct)
    {
        var path = GetSnapshotPath(ws.DagId);
        var json = Formatter.Format(snapshot);
        await WriteFileAtomicAsync(ws, path, json, ct);
    }

    private static async Task WriteFileAtomicAsync(DagWorkspacePaths ws, string targetPath, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ws.TmpDir);

        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content ?? string.Empty, Encoding.UTF8, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(tmp, targetPath, overwrite: true);
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
