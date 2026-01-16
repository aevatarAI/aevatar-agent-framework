using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.Platform.Core.Workflow;

// ============================================================
//  WorkflowEngine (MVP)
//
//  Purpose:
//  - Create a deterministic execution plan from MeshDefinition.
//  - Provide a minimal execution stub (real orchestration lands in later tasks).
//
//  Notes:
//  - We keep the plan bounded and deterministic (Kahn topo sort).
//  - This is intentionally minimal to avoid premature coupling to runtime.
// ============================================================
public sealed record WorkflowPlan(
    string PlanId,
    StrategyKind Strategy,
    BudgetSpec Budget,
    IReadOnlyList<NodeSpec> OrderedNodes,
    IReadOnlyList<EdgeSpec> Edges);

public sealed record WorkflowPlanResult(bool Ok, WorkflowPlan? Plan, IReadOnlyList<DslValidationError> Errors)
{
    public static WorkflowPlanResult Success(WorkflowPlan plan) => new(true, plan, Array.Empty<DslValidationError>());
    public static WorkflowPlanResult Failed(params DslValidationError[] errors)
        => new(false, null, errors ?? Array.Empty<DslValidationError>());
}

public sealed record WorkflowRunResult(string RunId, bool Ok, string Note);

public sealed class WorkflowEngine
{
    public WorkflowPlanResult Plan(MeshDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<DslValidationError>();
        var nodes = definition.Nodes ?? Array.Empty<NodeSpec>();
        var edges = definition.Edges ?? Array.Empty<EdgeSpec>();

        if (nodes.Count == 0)
            return WorkflowPlanResult.Failed(new DslValidationError("mesh.nodes_empty", "nodes 不可为空。", "nodes"));

        // Index nodes by id.
        var nodeById = new Dictionary<string, NodeSpec>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var id = (n?.Id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                errors.Add(new DslValidationError("node.id_required", "nodes[*].id 不可为空。", $"nodes[{i}].id"));
                continue;
            }

            if (nodeById.ContainsKey(id))
            {
                errors.Add(new DslValidationError("node.duplicate_id", $"节点 id 重复: '{id}'。", $"nodes[{i}].id"));
                continue;
            }

            nodeById[id] = n!;
        }

        if (errors.Count > 0)
            return WorkflowPlanResult.Failed(errors.ToArray());

        // Build adjacency for topo sort.
        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var indeg = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var id in nodeById.Keys)
        {
            outgoing[id] = new List<string>();
            indeg[id] = 0;
        }

        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            var from = (e?.From ?? string.Empty).Trim();
            var to = (e?.To ?? string.Empty).Trim();

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

            if (string.Equals(from, to, StringComparison.Ordinal))
            {
                errors.Add(new DslValidationError("edge.self_loop", $"节点 '{from}' 不允许自环 (from == to)。", $"edges[{i}]"));
                continue;
            }

            outgoing[from].Add(to);
            indeg[to] = indeg[to] + 1;
        }

        if (errors.Count > 0)
            return WorkflowPlanResult.Failed(errors.ToArray());

        var topo = TopoSort(outgoing, indeg);
        if (topo == null)
        {
            return WorkflowPlanResult.Failed(new DslValidationError(
                "mesh.cycle_detected",
                "Mesh 中存在环，无法生成拓扑序。",
                "edges"));
        }

        var orderedNodes = topo.Select(id => nodeById[id]).ToList().AsReadOnly();

        var plan = new WorkflowPlan(
            PlanId: $"plan_{Guid.NewGuid():N}",
            Strategy: definition.Strategy,
            Budget: definition.Budget,
            OrderedNodes: orderedNodes,
            Edges: edges.ToList().AsReadOnly());

        return WorkflowPlanResult.Success(plan);
    }

    public Task<WorkflowRunResult> ExecuteAsync(WorkflowPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        // MVP stub: execution is implemented in later tasks (RoleAgentFactory + ToolPolicy).
        return Task.FromResult(new WorkflowRunResult(
            RunId: $"run_{Guid.NewGuid():N}",
            Ok: true,
            Note: "Execution stub (MVP)."));
    }

    private static IReadOnlyList<string>? TopoSort(
        Dictionary<string, List<string>> outgoing,
        Dictionary<string, int> indeg)
    {
        var ready = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kv in indeg)
        {
            if (kv.Value == 0)
                ready.Add(kv.Key);
        }

        var result = new List<string>(outgoing.Count);
        var indegWork = new Dictionary<string, int>(indeg, StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            result.Add(id);

            var outs = outgoing[id];
            outs.Sort(StringComparer.Ordinal);
            foreach (var to in outs)
            {
                indegWork[to] = indegWork[to] - 1;
                if (indegWork[to] == 0)
                    ready.Add(to);
            }
        }

        return result.Count == outgoing.Count ? result : null;
    }
}


