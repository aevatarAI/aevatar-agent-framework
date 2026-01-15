using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Dag;

// ============================================================
//  DagExplain (aligned with AxiomReasoning semantics)
//
//  Ported spirit from:
//    apps/Aevatar.AxiomReasoning/src/Aevatar.AxiomReasoning/Graph/InMemoryGraphStore.cs
//
//  Semantics:
//  - Edges are "depends_on": from_id -> to_id  (dependency -> dependent)
//  - DirectDeps(node) = { from_id | edge.from_id -> node_id }
//  - MissingDeps = any visited dependency node whose type is Hypothesis/Assumption/Unknown
//  - Provable = !hasCycle && MissingDeps empty
// ============================================================

public static class DagExplain
{
    private const int MaxMissing = 200;
    private const int MaxTopo = 4000;

    public static SraDagExplain Explain(SraDagSnapshot snapshot, string nodeId)
    {
        snapshot ??= new SraDagSnapshot();
        nodeId = (nodeId ?? string.Empty).Trim();

        var result = new SraDagExplain
        {
            SessionId = snapshot.SessionId ?? string.Empty,
            NodeId = nodeId
        };

        if (nodeId.Length == 0)
            return result;

        // Index nodes by id
        var nodes = new Dictionary<string, SraDagNode>(StringComparer.Ordinal);
        foreach (var n in snapshot.Nodes)
        {
            if (n is null) continue;
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            nodes[id] = n;
        }

        if (!nodes.TryGetValue(nodeId, out var node))
            return result; // not found

        result.Node = node;

        // Build incoming adjacency: to -> [from]
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in snapshot.Edges)
        {
            var to = (e?.ToId ?? string.Empty).Trim();
            var from = (e?.FromId ?? string.Empty).Trim();
            if (to.Length == 0 || from.Length == 0) continue;

            if (!deps.TryGetValue(to, out var list))
            {
                list = new List<string>();
                deps[to] = list;
            }
            list.Add(from);
        }

        if (deps.TryGetValue(nodeId, out var direct))
        {
            result.DirectDeps.AddRange(direct.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        }

        // DFS for cycle detection + topo order
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var topo = new List<string>();
        var hasCycle = false;

        void Dfs(string cur)
        {
            if (hasCycle) return;
            if (topo.Count >= MaxTopo) return; // hard cap

            if (stack.Contains(cur))
            {
                hasCycle = true;
                return;
            }
            if (visited.Contains(cur))
                return;

            visited.Add(cur);
            stack.Add(cur);

            if (deps.TryGetValue(cur, out var prev))
            {
                foreach (var p in prev)
                {
                    Dfs(p);
                    if (hasCycle) return;
                    if (topo.Count >= MaxTopo) return;
                }
            }

            stack.Remove(cur);
            topo.Add(cur);
        }

        Dfs(nodeId);
        // Post-order already guarantees dependencies appear before dependents.
        result.Topo.AddRange(topo);

        // Missing dependencies in closure (excluding target itself)
        var missing = new List<SraDagNode>();
        foreach (var id in visited)
        {
            if (string.Equals(id, nodeId, StringComparison.Ordinal)) continue;
            if (!nodes.TryGetValue(id, out var n2)) continue;

            if (n2.Type is SraDagNodeType.Hypothesis
                or SraDagNodeType.Assumption
                or SraDagNodeType.Unknown)
            {
                missing.Add(n2);
                if (missing.Count >= MaxMissing) break;
            }
        }

        result.HasCycle = hasCycle;
        result.Missing.AddRange(missing
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Id, StringComparer.Ordinal));
        result.Provable = !hasCycle && missing.Count == 0;

        return result;
    }
}


