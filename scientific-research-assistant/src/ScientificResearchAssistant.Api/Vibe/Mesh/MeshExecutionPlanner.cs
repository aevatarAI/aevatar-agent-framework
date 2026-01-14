using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace ScientificResearchAssistant.Api.Vibe.Mesh;

// ============================================================
//  MeshExecutionPlanner
//
//  Input:
//  - A validated MeshDefinition (still treated defensively).
//
//  Output:
//  - A deterministic MeshExecutionPlan:
//    - topo order
//    - per-node inbound bindings (from edges)
//    - budget snapshot
//
//  Philosophy:
//  - Fail early, fail actionable: return structured validation errors, no partial plans.
// ============================================================

public sealed record MeshPlanResult(bool Ok, MeshExecutionPlan? Plan, IReadOnlyList<DslValidationError> Errors)
{
    public static MeshPlanResult Success(MeshExecutionPlan plan) => new(true, plan, Array.Empty<DslValidationError>());
    public static MeshPlanResult Failed(params DslValidationError[] errors) => new(false, null, errors ?? Array.Empty<DslValidationError>());
}

public sealed class MeshExecutionPlanner
{
    private readonly GlobalAgentYamlRegistry _roles;

    public MeshExecutionPlanner(GlobalAgentYamlRegistry roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public MeshPlanResult Plan(string sessionId, string runId, MeshDefinition definition)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        runId = (runId ?? string.Empty).Trim();
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<DslValidationError>();

        // Validate nodes (defensive; compiler should already do basics).
        var nodes = definition.Nodes ?? Array.Empty<NodeSpec>();
        var nodeById = new Dictionary<string, NodeSpec>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var id = (n?.Id ?? string.Empty).Trim();
            var type = (n?.Type ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                errors.Add(new DslValidationError("node.id_required", "nodes[*].id 不可为空。", $"nodes[{i}].id"));
                continue;
            }

            if (nodeById.ContainsKey(id))
            {
                // Compiler also checks duplicates, but keep a stable planner error code for callers.
                errors.Add(new DslValidationError("node.duplicate_id", $"节点 id 重复: '{id}'。", $"nodes[{i}].id"));
                continue;
            }

            // Built-in types are always allowed; otherwise require role YAML in ~/.aevatar/agents.
            if (!SraMeshMappings.IsSupportedNodeType(type) && !_roles.HasRole(type))
            {
                var known = new List<string>();
                known.AddRange(SraMeshMappings.AllowedNodeTypes);
                known.AddRange(_roles.GetKnownRoles());
                known = known
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .ToList();

                errors.Add(new DslValidationError(
                    "node.unsupported_type",
                    $"节点 '{id}' 使用了不支持的 type '{type}'. 允许: [{string.Join(", ", known)}]（以及 ~/.aevatar/agents/*.yaml 中的 role）.",
                    $"nodes[{i}].type"));
                continue;
            }

            nodeById[id] = n!;
        }

        // Validate edges + build adjacency
        var edges = definition.Edges ?? Array.Empty<EdgeSpec>();
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indeg = new Dictionary<string, int>(StringComparer.Ordinal);
        var inbound = new Dictionary<string, List<MeshBinding>>(StringComparer.Ordinal);

        foreach (var id in nodeById.Keys)
        {
            outgoing[id] = new List<string>();
            indeg[id] = 0;
            inbound[id] = new List<MeshBinding>();
        }

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            var from = (e?.From ?? string.Empty).Trim();
            var to = (e?.To ?? string.Empty).Trim();
            var channel = (e?.Channel ?? string.Empty).Trim();

            if (from.Length == 0 || to.Length == 0)
            {
                errors.Add(new DslValidationError("edge.endpoint_required", "edge.from / edge.to 不可为空。", $"edges[{i}]"));
                continue;
            }

            if (!nodeById.ContainsKey(from))
            {
                errors.Add(new DslValidationError("edge.from_missing", $"edge.from 引用了不存在的节点 '{from}'。", $"edges[{i}].from"));
                continue;
            }

            if (!nodeById.ContainsKey(to))
            {
                errors.Add(new DslValidationError("edge.to_missing", $"edge.to 引用了不存在的节点 '{to}'。", $"edges[{i}].to"));
                continue;
            }

            if (!SraMeshMappings.IsSupportedChannel(channel))
            {
                errors.Add(new DslValidationError(
                    "edge.unsupported_channel",
                    $"edge.channel '{channel}' 不支持。允许: [{string.Join(", ", SraMeshMappings.AllowedChannels)}].",
                    $"edges[{i}].channel"));
                continue;
            }

            if (string.Equals(from, to, StringComparison.Ordinal))
            {
                errors.Add(new DslValidationError("edge.self_loop", $"节点 '{from}' 不允许自环 (from == to)。", $"edges[{i}]"));
                continue;
            }

            outgoing[from].Add(to);
            indeg[to] = indeg[to] + 1;
            inbound[to].Add(new MeshBinding(from, channel));
        }

        // Budget snapshot (defensive clamp)
        var maxSteps = Math.Clamp(definition.Budget?.MaxSteps ?? 0, 1, 100_000);
        var tokenLimit = Math.Clamp(definition.Budget?.TokenLimit ?? 0, 1, 200_000_000);

        // If any validation errors, stop here.
        if (errors.Count > 0)
            return new MeshPlanResult(false, null, errors);

        // Deterministic topo sort (Kahn + lexical tie-break).
        var ready = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kv in indeg)
        {
            if (kv.Value == 0)
                ready.Add(kv.Key);
        }

        var topo = new List<string>(nodeById.Count);
        var indegWork = new Dictionary<string, int>(indeg, StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            topo.Add(id);

            // Ensure deterministic traversal of outgoing edges too.
            var outs = outgoing[id];
            outs.Sort(StringComparer.Ordinal);
            foreach (var v in outs)
            {
                indegWork[v] = indegWork[v] - 1;
                if (indegWork[v] == 0)
                    ready.Add(v);
            }
        }

        if (topo.Count != nodeById.Count)
        {
            // Cycle detected: collect remaining nodes (bounded).
            var remaining = indegWork
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Take(30)
                .ToList();

            return MeshPlanResult.Failed(new DslValidationError(
                "mesh.cycle_detected",
                $"Mesh 中存在环，无法生成拓扑序。涉及节点（最多30个）: {string.Join(", ", remaining)}",
                "edges"));
        }

        // Build plan nodes list (keep stable order by node id).
        var planNodes = nodeById
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                var id = kv.Key;
                var n = kv.Value;
                var binds = inbound[id]
                    .OrderBy(b => b.FromNodeId, StringComparer.Ordinal)
                    .ThenBy(b => b.Channel, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly();

                return new MeshPlanNode(
                    Id: id,
                    Type: n.Type,
                    Params: n.Params,
                    Inbound: binds);
            })
            .ToList()
            .AsReadOnly();

        var plan = new MeshExecutionPlan(
            SessionId: sessionId,
            RunId: runId,
            DslVersion: definition.DslVersion ?? string.Empty,
            Strategy: definition.Strategy,
            BudgetMaxSteps: maxSteps,
            BudgetTokenLimit: tokenLimit,
            Nodes: planNodes,
            TopoOrder: topo.AsReadOnly());

        return MeshPlanResult.Success(plan);
    }
}


