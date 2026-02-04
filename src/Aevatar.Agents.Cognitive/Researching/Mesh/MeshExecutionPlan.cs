using Aevatar.CognitiveMesh.Dsl.Models;

namespace Aevatar.Agents.Cognitive.Researching.Mesh;

// ============================================================
//  MeshExecutionPlan (API-internal)
//
//  Purpose:
//  - Deterministic, bounded "compiled plan" derived from MeshDefinition.
//  - Used by MeshExecutionRunner to execute nodes in topo order and route outputs.
// ============================================================

public sealed record MeshBinding(string FromNodeId, string Channel);

public sealed record MeshPlanNode(
    string Id,
    string Type,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Params,
    IReadOnlyList<MeshBinding> Inbound);

public sealed record MeshExecutionPlan(
    string SessionId,
    string RunId,
    string DslVersion,
    StrategyKind Strategy,
    int BudgetMaxSteps,
    int BudgetTokenLimit,
    IReadOnlyList<MeshPlanNode> Nodes,
    IReadOnlyList<string> TopoOrder);


