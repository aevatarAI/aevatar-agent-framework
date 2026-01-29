using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Sessions.Runtime;

public sealed record WorkflowGraphSnapshot(
    string WorkflowId,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowEdgeDto> Edges,
    DateTimeOffset UpdatedAt);

public sealed record WorkflowNodeDto(
    string Id,
    string Type,
    string Label,
    int Depth,
    IReadOnlyDictionary<string, object?>? Meta = null);

public sealed record WorkflowEdgeDto(
    string From,
    string To,
    string Channel);

public sealed record WorkflowLoadResult(
    bool Ok,
    WorkflowGraphSnapshot? Graph,
    IReadOnlyList<DslValidationError> Errors,
    IReadOnlyList<WorkflowAgentInfo> Agents);

public sealed record WorkflowAgentInfo(
    string NodeId,
    string Role,
    string ActorId);

public sealed class WorkflowMeshService
{
    private readonly IGAgentActorManager _actorManager;
    private readonly RoleAgentFactory _roleAgentFactory;
    private readonly WorkflowMeshCompiler _compiler;
    private readonly ILogger<WorkflowMeshService> _logger;

    private WorkflowGraphSnapshot? _latest;

    public WorkflowMeshService(
        IGAgentActorManager actorManager,
        RoleAgentFactory roleAgentFactory,
        WorkflowMeshCompiler compiler,
        ILogger<WorkflowMeshService> logger)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
        _roleAgentFactory = roleAgentFactory ?? throw new ArgumentNullException(nameof(roleAgentFactory));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public WorkflowGraphSnapshot? GetLatestGraph()
        => _latest;

    public async Task<WorkflowLoadResult> LoadFromYamlAsync(string yaml, CancellationToken ct = default)
    {
        var compile = _compiler.Compile(yaml);
        if (!compile.Ok || compile.Definition == null)
        {
            return new WorkflowLoadResult(false, null, compile.Errors, Array.Empty<WorkflowAgentInfo>());
        }

        var def = compile.Definition;
        var agents = await InstantiateAgentsAsync(def, ct);
        var graph = BuildGraph(def);
        _latest = graph;

        return new WorkflowLoadResult(true, graph, Array.Empty<DslValidationError>(), agents);
    }

    private async Task<IReadOnlyList<WorkflowAgentInfo>> InstantiateAgentsAsync(MeshDefinition def, CancellationToken ct)
    {
        var results = new List<WorkflowAgentInfo>();
        foreach (var node in def.Nodes)
        {
            var rawId = (node.Id ?? string.Empty).Trim();
            if (rawId.Length == 0)
                continue;

            var role = ResolveRole(node);
            try
            {
                var actor = await _actorManager.CreateAndRegisterAsync<RoleAIGAgent>(rawId, ct);
                if (actor.GetAgent() is RoleAIGAgent roleAgent)
                {
                    roleAgent.InitializeRole(role);
                    if (!string.IsNullOrWhiteSpace(role))
                        await _roleAgentFactory.ApplyYamlAsync(roleAgent, role, ct);
                }

                results.Add(new WorkflowAgentInfo(rawId, role, actor.Id));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create workflow agent {NodeId} ({Role})", node.Id, role);
            }
        }

        return results;
    }

    private static string ResolveRole(NodeSpec node)
    {
        if (node.Params != null &&
            node.Params.TryGetValue("role", out var roleElem) &&
            roleElem.ValueKind == JsonValueKind.String)
        {
            var role = (roleElem.GetString() ?? string.Empty).Trim();
            if (role.Length > 0) return role;
        }

        var type = (node.Type ?? string.Empty).Trim();
        return type.Length == 0 ? "role" : type;
    }

    private static WorkflowGraphSnapshot BuildGraph(MeshDefinition def)
    {
        var nodeList = def.Nodes.ToList();
        var edgeList = def.Edges.ToList();
        var depth = BuildDepthMap(nodeList, edgeList);

        var nodes = nodeList.Select(node =>
                new WorkflowNodeDto(
                    Id: node.Id,
                    Type: node.Type,
                    Label: $"{node.Id} · {node.Type}",
                    Depth: depth.TryGetValue(node.Id, out var d) ? d : 0))
            .ToList();

        var edges = edgeList.Select(edge =>
                new WorkflowEdgeDto(edge.From, edge.To, edge.Channel))
            .ToList();

        return new WorkflowGraphSnapshot(
            WorkflowId: def.Goal?.Name ?? "workflow",
            Nodes: nodes,
            Edges: edges,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, int> BuildDepthMap(
        IReadOnlyList<NodeSpec> nodes,
        IReadOnlyList<EdgeSpec> edges)
    {
        var depth = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var indeg = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var e in edges)
        {
            if (!outgoing.TryGetValue(e.From, out var list)) continue;
            list.Add(e.To);
            if (indeg.ContainsKey(e.To))
                indeg[e.To] += 1;
        }

        var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!outgoing.TryGetValue(current, out var outs)) continue;
            foreach (var next in outs)
            {
                depth[next] = Math.Max(depth[next], depth[current] + 1);
                indeg[next] -= 1;
                if (indeg[next] == 0)
                    queue.Enqueue(next);
            }
        }

        return depth;
    }
}
