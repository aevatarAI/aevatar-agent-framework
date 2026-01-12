using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Aevatar.AxiomReasoning.EventStreaming.Events;
using Aevatar.AxiomReasoning.Graph.Models;

namespace Aevatar.AxiomReasoning.Graph;

// ============================================================
//  AXIOM DAG SERVICE (Graph DB)
//
//  职责：
//  - 存储公理/定理/假设的 DAG（按 sessionId 分区）
//  - 提供基础 DAG 推理：依赖闭包、缺失依赖、可证明性、环检测
//
//  设计原则：
//  - 轻量内置（无外部依赖，先跑起来）
//  - 数据结构稳定：node/edge 都是可序列化对象（API 输出）
// ============================================================

public sealed class InMemoryGraphStore : IGraphStore
{
    private static readonly Regex AxiomIdRegex = new(@"^([A-Za-z]\w*)\s*:", RegexOptions.Compiled);

    private sealed class Graph
    {
        public ConcurrentDictionary<string, Node> Nodes { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, Edge> Edges { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<string, Graph> _graphs = new(StringComparer.Ordinal);

    public object GetDiagnostics()
    {
        var sessions = _graphs.Count;
        long nodes = 0;
        long edges = 0;
        foreach (var g in _graphs.Values)
        {
            nodes += g.Nodes.Count;
            edges += g.Edges.Count;
        }

        return new
        {
            type = "inmemory",
            sessions,
            totals = new
            {
                nodes,
                edges
            }
        };
    }

    public void UpsertFromGraphEvent(string sessionId, GraphEvent graphEvent)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var g = _graphs.GetOrAdd(sessionId, _ => new Graph());

        // 1) Upsert axioms
        var axiomIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < graphEvent.Axioms.Count; i++)
        {
            var line = graphEvent.Axioms[i] ?? "";
            var id = ExtractAxiomId(line, i);
            axiomIds.Add(id);
            g.Nodes[id] = new Node
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
            g.Nodes[id] = new Node
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

            g.Nodes[id] = new Node
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
                if (!g.Nodes.TryGetValue(fromId, out var existing))
                {
                    var type = axiomIds.Contains(fromId)
                        ? NodeType.Axiom
                        : assumptionIds.Contains(fromId)
                            ? NodeType.Assumption
                        : theoremIds.Contains(fromId)
                            ? NodeType.Theorem
                            : NodeType.Hypothesis;

                    g.Nodes[fromId] = new Node
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
                        g.Nodes[fromId] = existing with { Type = NodeType.Axiom, UpdatedAt = DateTimeOffset.UtcNow };
                    else if (assumptionIds.Contains(fromId) && existing.Type != NodeType.Assumption)
                        g.Nodes[fromId] = existing with { Type = NodeType.Assumption, UpdatedAt = DateTimeOffset.UtcNow };
                    else if (theoremIds.Contains(fromId) && existing.Type != NodeType.Theorem)
                        g.Nodes[fromId] = existing with { Type = NodeType.Theorem, UpdatedAt = DateTimeOffset.UtcNow };
                }

                var key = $"{fromId}->{toId}";
                g.Edges[key] = new Edge { FromId = fromId, ToId = toId, Type = "depends_on" };
            }
        }
    }

    // ============================================================
    //  IGraphStore (async wrapper)
    // ============================================================

    public Task UpsertFromGraphEventAsync(string sessionId, GraphEvent graphEvent, CancellationToken ct = default)
    {
        // InMemory store is synchronous; keep signature async for backend swap (Supabase/Neo4j).
        UpsertFromGraphEvent(sessionId, graphEvent);
        return Task.CompletedTask;
    }

    public Task<Snapshot> GetSnapshotAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(GetSnapshot(sessionId));

    public Task<ExplainResult> ExplainAsync(string sessionId, string nodeId, CancellationToken ct = default)
        => Task.FromResult(Explain(sessionId, nodeId));

    public Snapshot GetSnapshot(string sessionId)
    {
        if (!_graphs.TryGetValue(sessionId, out var g))
            return new Snapshot { SessionId = sessionId };

        return new Snapshot
        {
            SessionId = sessionId,
            Nodes = g.Nodes.Values.OrderBy(n => n.Type).ThenBy(n => n.Id).ToList(),
            Edges = g.Edges.Values.OrderBy(e => e.FromId).ThenBy(e => e.ToId).ToList()
        };
    }

    public ExplainResult Explain(string sessionId, string nodeId)
    {
        if (!_graphs.TryGetValue(sessionId, out var g))
            return new ExplainResult { SessionId = sessionId, Node = null };

        if (!g.Nodes.TryGetValue(nodeId, out var node))
            return new ExplainResult { SessionId = sessionId, Node = null };

        // Build incoming adjacency: to -> [from]
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in g.Edges.Values)
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
            if (!g.Nodes.TryGetValue(id, out var n2)) continue;
            if (n2.Type is NodeType.Hypothesis or NodeType.Assumption or NodeType.Unknown)
                missing.Add(n2);
        }

        // Provable = no cycle AND no missing dependency kinds
        var provable = !hasCycle && missing.Count == 0;

        // TopologicalOrder: dependencies first; keep stable order (axioms first, then theorems, then target)
        topo.Reverse(); // now dependencies before dependents

        return new ExplainResult
        {
            SessionId = sessionId,
            Node = node,
            DirectDependencies = direct,
            TopologicalOrder = topo,
            HasCycle = hasCycle,
            ProvableFromAxioms = provable,
            MissingDependencies = missing
        };
    }

    private static string ExtractAxiomId(string line, int idx)
    {
        if (string.IsNullOrWhiteSpace(line)) return $"A{idx + 1}";
        var m = AxiomIdRegex.Match(line.Trim());
        return m.Success ? m.Groups[1].Value : $"A{idx + 1}";
    }
}