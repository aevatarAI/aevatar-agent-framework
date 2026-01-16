using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;
using Aevatar.AxiomReasoning.EventStreaming.Events;
using Aevatar.AxiomReasoning.Graph.Models;

namespace Aevatar.AxiomReasoning.Graph;

// ============================================================
//  NEO4J GRAPH STORE
//
//  目标：
//  - 与 InMemoryGraphStore（AxiomDagService）保持一致的业务语义：
//    - GraphEvent → Nodes/Edges 的构建逻辑一致
//    - Explain 的推理逻辑一致（closure/cycle/provable/missing）
//  - 底层存储：只通过 IGraphClient（由 Aevatar.Agents.Persistence.Neo4j.Graph 注册）
// ============================================================
public sealed class Neo4jGraphStore : IGraphStore
{
    private const string NodeLabel = "AxiomDagNode";
    private const string EdgeType = "DEPENDS_ON";
    private const string DefaultEdgeKind = "depends_on";

    private readonly IGraphClient _graph;
    private readonly ILogger<Neo4jGraphStore> _logger;

    // Hot cache (per-process) to reduce DB reads.
    private readonly ConcurrentDictionary<string, Snapshot> _cache = new(StringComparer.Ordinal);

    // Serialize writes per session to avoid interleaving writes from concurrent progress callbacks.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);

    public Neo4jGraphStore(IGraphClient graph, ILogger<Neo4jGraphStore> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    public object GetDiagnostics() => new
    {
        type = "neo4j",
        cache = new { sessions = _cache.Count },
        schema = new { nodeLabel = NodeLabel, edgeType = EdgeType }
    };

    public async Task UpsertFromGraphEventAsync(string sessionId, GraphEvent graphEvent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        // Keep semantics aligned with InMemoryGraphStore:
        // - Upsert nodes/edges; do NOT delete missing edges/nodes (incremental merge behavior).
        //
        // IMPORTANT (UI consistency):
        // - UI renders the latest graph snapshot from AG-UI (STATE_SNAPSHOT/STATE_DELTA).
        // - Graph DB writes (Neo4j) can lag behind due to serialized writes per session.
        // - If we only update _cache AFTER acquiring the write lock, /dag/{nodeId} may read stale snapshot
        //   and return ExplainResult.node = null ("Node not found in DAG") even though UI shows the node.
        // - Therefore we always update _cache first (latest-wins), then serialize DB writes.

        Snapshot snapshot;
        try
        {
            var (nodes, edges) = BuildDag(graphEvent);
            snapshot = new Snapshot
            {
                SessionId = sessionId,
                Nodes = nodes.OrderBy(n => n.Type).ThenBy(n => n.Id).ToList(),
                Edges = edges.OrderBy(e => e.FromId).ThenBy(e => e.ToId).ToList()
            };

            _cache[sessionId] = snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neo4jGraphStore build-snapshot failed (session={SessionId})", sessionId);
            return;
        }

        var gate = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow.ToString("O");

            // ─────────────────────────────────────────────
            // Nodes UPSERT (MERGE by deterministic "id")
            // ─────────────────────────────────────────────
            foreach (var n in snapshot.Nodes)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(n.Id)) continue;

                var nodeId = NodeIdFor(sessionId, n.Id);
                await _graph.WriteAsync(
                    NodeLabel,
                    new Dictionary<string, Value>
                    {
                        ["id"] = new StringValue(nodeId.Value),
                        ["session_id"] = new StringValue(sessionId),
                        ["node_id"] = new StringValue(n.Id),
                        ["kind"] = new StringValue(n.Type.ToString()),
                        ["label"] = new StringValue(n.Label ?? ""),
                        ["proof"] = new StringValue(n.Proof ?? ""),
                        ["updated_at"] = new StringValue(now)
                    });
            }

            // ─────────────────────────────────────────────
            // Edges UPSERT (MERGE by deterministic "id")
            // NOTE: GraphMapper maps From/To using Neo4j ElementId, so we persist domain ids as properties.
            // ─────────────────────────────────────────────
            foreach (var e in snapshot.Edges)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(e.FromId) || string.IsNullOrWhiteSpace(e.ToId)) continue;

                var from = NodeIdFor(sessionId, e.FromId);
                var to = NodeIdFor(sessionId, e.ToId);
                var edgeId = EdgeIdFor(sessionId, e.FromId, e.ToId);

                await _graph.WriteAsync(
                    EdgeType,
                    from,
                    to,
                    new Dictionary<string, Value>
                    {
                        ["id"] = new StringValue(edgeId.Value),
                        ["session_id"] = new StringValue(sessionId),
                        ["from_id"] = new StringValue(e.FromId),
                        ["to_id"] = new StringValue(e.ToId),
                        ["kind"] = new StringValue(string.IsNullOrWhiteSpace(e.Type) ? DefaultEdgeKind : e.Type),
                        ["updated_at"] = new StringValue(now)
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neo4jGraphStore upsert failed (session={SessionId})", sessionId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Snapshot> GetSnapshotAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new Snapshot { SessionId = sessionId ?? "" };

        if (_cache.TryGetValue(sessionId, out var cached))
            return cached;

        try
        {
            ct.ThrowIfCancellationRequested();

            var nodes = await _graph.QueryAsync(new NodeQuery
            {
                Type = NodeLabel,
                Conditions =
                [
                    new Condition("session_id", Operator.Equals, new StringValue(sessionId))
                ]
            });

            var edges = await _graph.QueryAsync(new EdgeQuery
            {
                Type = EdgeType,
                Conditions =
                [
                    new Condition("session_id", Operator.Equals, new StringValue(sessionId))
                ]
            });

            var snapNodes = nodes
                .Select(ToNode)
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .OrderBy(n => n.Type)
                .ThenBy(n => n.Id)
                .ToList();

            var snapEdges = edges
                .Select(ToEdge)
                .Where(e => !string.IsNullOrWhiteSpace(e.FromId) && !string.IsNullOrWhiteSpace(e.ToId))
                .OrderBy(e => e.FromId)
                .ThenBy(e => e.ToId)
                .ToList();

            var snapshot = new Snapshot
            {
                SessionId = sessionId,
                Nodes = snapNodes,
                Edges = snapEdges
            };

            _cache[sessionId] = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neo4jGraphStore read failed (session={SessionId})", sessionId);
            return new Snapshot { SessionId = sessionId };
        }
    }

    public async Task<ExplainResult> ExplainAsync(string sessionId, string nodeId, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(sessionId, ct);
        return ExplainFromSnapshot(snapshot, nodeId);
    }

    // ============================================================
    //  Logic: keep consistent with InMemoryGraphStore
    // ============================================================

    private static (List<Node> nodes, List<Edge> edges) BuildDag(GraphEvent graphEvent)
    {
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var edges = new Dictionary<string, Edge>(StringComparer.Ordinal);

        // 1) Upsert axioms
        var axiomIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < graphEvent.Axioms.Count; i++)
        {
            var line = graphEvent.Axioms[i] ?? "";
            var id = InMemoryGraphStoreHelpers.ExtractAxiomId(line, i);
            axiomIds.Add(id);
            nodes[id] = new Node
            {
                Id = id,
                Type = NodeType.Axiom,
                Label = line,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        // 1.5) Upsert assumptions (e.g. S1)
        var assumptionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in graphEvent.Assumptions ?? [])
        {
            var id = (a.Id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id)) continue;
            assumptionIds.Add(id);
            nodes[id] = new Node
            {
                Id = id,
                Type = NodeType.Assumption,
                Label = a.Statement ?? "",
                // Store motivation as "proof" to show in inspector (assumptions have no proof).
                Proof = a.Motivation ?? "",
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        // 2) Upsert theorems
        var theoremIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in graphEvent.Theorems ?? [])
        {
            var id = (t.Id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id)) continue;
            theoremIds.Add(id);

            nodes[id] = new Node
            {
                Id = id,
                Type = NodeType.Theorem,
                Label = t.Statement ?? "",
                Proof = t.Proof ?? "",
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        // 3) Upsert edges + unknown deps as hypothesis
        foreach (var t in graphEvent.Theorems ?? [])
        {
            var toId = (t.Id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(toId)) continue;

            foreach (var depRaw in t.DependsOn ?? [])
            {
                var fromId = (depRaw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(fromId)) continue;

                // Ensure dependency node exists with a reasonable kind
                if (!nodes.TryGetValue(fromId, out var existing))
                {
                    var type = axiomIds.Contains(fromId)
                        ? NodeType.Axiom
                        : assumptionIds.Contains(fromId)
                            ? NodeType.Assumption
                            : theoremIds.Contains(fromId)
                                ? NodeType.Theorem
                                : NodeType.Hypothesis;

                    nodes[fromId] = new Node
                    {
                        Id = fromId,
                        Type = type,
                        Label = fromId,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                }
                else
                {
                    // Fix-up kind if we later learn it is an axiom/theorem
                    if (axiomIds.Contains(fromId) && existing.Type != NodeType.Axiom)
                        nodes[fromId] = existing with { Type = NodeType.Axiom, UpdatedAt = DateTimeOffset.UtcNow };
                    else if (assumptionIds.Contains(fromId) && existing.Type != NodeType.Assumption)
                        nodes[fromId] = existing with { Type = NodeType.Assumption, UpdatedAt = DateTimeOffset.UtcNow };
                    else if (theoremIds.Contains(fromId) && existing.Type != NodeType.Theorem)
                        nodes[fromId] = existing with { Type = NodeType.Theorem, UpdatedAt = DateTimeOffset.UtcNow };
                }

                var key = $"{fromId}->{toId}";
                edges[key] = new Edge { FromId = fromId, ToId = toId, Type = DefaultEdgeKind };
            }
        }

        return (nodes.Values.ToList(), edges.Values.ToList());
    }

    private static ExplainResult ExplainFromSnapshot(Snapshot snapshot, string nodeId)
    {
        var byId = snapshot.Nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
        byId.TryGetValue(nodeId, out var node);

        if (node is null)
            return new ExplainResult { SessionId = snapshot.SessionId, Node = null };

        // Build incoming adjacency: to -> [from]
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in snapshot.Edges)
        {
            if (!deps.TryGetValue(e.ToId, out var list))
            {
                list = new List<string>();
                deps[e.ToId] = list;
            }
            list.Add(e.FromId);
        }

        var direct = deps.TryGetValue(nodeId, out var d0) ? d0.Distinct().OrderBy(x => x).ToList() : [];

        // DFS for cycle detection + topo order on the dependency closure
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var topo = new List<string>();
        var hasCycle = false;

        void Dfs(string cur)
        {
            if (hasCycle) return;
            if (stack.Contains(cur))
            {
                hasCycle = true;
                return;
            }
            if (visited.Contains(cur)) return;

            visited.Add(cur);
            stack.Add(cur);

            if (deps.TryGetValue(cur, out var prev))
            {
                foreach (var p in prev)
                {
                    Dfs(p);
                    if (hasCycle) return;
                }
            }

            stack.Remove(cur);
            topo.Add(cur);
        }

        Dfs(nodeId);

        // Missing dependencies = any Hypothesis/Assumption/Unknown in closure (excluding the target itself)
        var missing = new List<Node>();
        foreach (var id in visited)
        {
            if (string.Equals(id, nodeId, StringComparison.Ordinal)) continue;
            if (!byId.TryGetValue(id, out var n2)) continue;
            if (n2.Type is NodeType.Hypothesis or NodeType.Assumption or NodeType.Unknown)
                missing.Add(n2);
        }

        // Provable = no cycle AND no missing dependency kinds
        var provable = !hasCycle && missing.Count == 0;

        // TopologicalOrder: dependencies first
        topo.Reverse();

        return new ExplainResult
        {
            SessionId = snapshot.SessionId,
            Node = node,
            DirectDependencies = direct,
            TopologicalOrder = topo,
            HasCycle = hasCycle,
            ProvableFromAxioms = provable,
            MissingDependencies = missing
        };
    }

    private static Node ToNode(GraphNode n)
    {
        var nodeId = TryGetString(n.Properties, "node_id") ?? n.Id.Value;
        var kind = TryGetString(n.Properties, "kind");
        var label = TryGetString(n.Properties, "label") ?? "";
        var proof = TryGetString(n.Properties, "proof") ?? "";
        var updatedAt = ParseTimestamp(TryGetString(n.Properties, "updated_at"));

        return new Node
        {
            Id = nodeId,
            Type = ParseNodeType(kind),
            Label = label,
            Proof = proof,
            UpdatedAt = updatedAt
        };
    }

    private static Edge ToEdge(GraphEdge e)
    {
        var fromId = TryGetString(e.Properties, "from_id") ?? "";
        var toId = TryGetString(e.Properties, "to_id") ?? "";
        var kind = TryGetString(e.Properties, "kind") ?? DefaultEdgeKind;

        return new Edge
        {
            FromId = fromId,
            ToId = toId,
            Type = kind
        };
    }

    private static string? TryGetString(IReadOnlyDictionary<string, Value> props, string key)
    {
        if (!props.TryGetValue(key, out var v)) return null;
        return v switch
        {
            StringValue s => s.Data,
            IntValue i => i.Data.ToString(),
            BoolValue b => b.Data ? "true" : "false",
            FloatValue f => f.Data.ToString("R"),
            _ => null
        };
    }

    private static DateTimeOffset ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTimeOffset.UtcNow;
        return DateTimeOffset.TryParse(raw, out var dt) ? dt : DateTimeOffset.UtcNow;
    }

    private static NodeType ParseNodeType(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return NodeType.Unknown;
        return Enum.TryParse<NodeType>(kind, ignoreCase: true, out var t) ? t : NodeType.Unknown;
    }

    private static NodeId NodeIdFor(string sessionId, string nodeId) => new($"{sessionId}:{nodeId}");

    private static EdgeId EdgeIdFor(string sessionId, string fromId, string toId) => new($"{sessionId}:{fromId}->{toId}");

    // Keep axiom-id extraction identical to InMemoryGraphStore.
    private static class InMemoryGraphStoreHelpers
    {
        // NOTE:
        // - This MUST match InMemoryGraphStore + frontend regex.
        // - Use single backslashes in C# verbatim string: \w and \s.
        private static readonly Regex AxiomIdRegex = new(@"^([A-Za-z]\w*)\s*:", RegexOptions.Compiled);

        public static string ExtractAxiomId(string line, int idx)
        {
            if (string.IsNullOrWhiteSpace(line)) return $"A{idx + 1}";
            var m = AxiomIdRegex.Match(line.Trim());
            return m.Success ? m.Groups[1].Value : $"A{idx + 1}";
        }
    }
}


