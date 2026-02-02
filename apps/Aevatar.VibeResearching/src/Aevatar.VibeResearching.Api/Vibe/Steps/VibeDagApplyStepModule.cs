using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.AGUI;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeDagApplyStepModule : VibeStepModuleBase
{
    private readonly DagStore _dag;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibeDagApplyStepModule> _logger;

    public VibeDagApplyStepModule(
        DagStore dag,
        ResearchSessionManager sessions,
        ILogger<VibeDagApplyStepModule> logger)
    {
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_dag_apply";
    public override string StepType => "vibe_dag_apply";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_dag_apply requires session_id");

        var candidateKey = ResolveStringParameter(step.Parameters, "candidate_var", "dag_builder");
        var consensusKey = ResolveStringParameter(step.Parameters, "consensus_var", "dag_consensus");

        var candidateRaw = ResolveStringVar(coordinator, candidateKey);
        if (string.IsNullOrWhiteSpace(candidateRaw))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["accepted"] = false,
                ["blocked"] = false,
                ["parse_failed"] = false,
                ["red_flags"] = new List<string>()
            });
        }

        var session = _sessions.GetOrCreate(sessionId);
        var dagId = session.EffectiveDagId;
        var currentDag = await _dag.LoadSnapshotAsync(dagId, ct);
        var mutation = VibeWorkflowParsing.TryParseDagBuilderCandidate(sessionId, candidateRaw, currentDag);
        if (mutation == null)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["accepted"] = false,
                ["blocked"] = false,
                ["parse_failed"] = true,
                ["red_flags"] = new List<string>()
            });
        }

        var consensusObj = ResolveConsensus(coordinator, consensusKey);
        var accepted = consensusObj.Accepted;
        var redFlags = consensusObj.RedFlags;

        if (!accepted)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["accepted"] = false,
                ["blocked"] = redFlags.Count > 0,
                ["parse_failed"] = false,
                ["red_flags"] = redFlags,
                ["mutation_id"] = mutation.MutationId ?? string.Empty,
                ["nodes"] = mutation.UpsertNodes.Count,
                ["edges"] = mutation.UpsertEdges.Count
            });
        }

        if (mutation.UpsertNodes.Count == 0 && mutation.UpsertEdges.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["accepted"] = true,
                ["blocked"] = false,
                ["parse_failed"] = false,
                ["red_flags"] = redFlags,
                ["mutation_id"] = mutation.MutationId ?? string.Empty,
                ["nodes"] = 0,
                ["edges"] = 0,
                ["applied"] = false,
                ["accepted_nodes"] = new List<Dictionary<string, object?>>()
            });
        }

        var applied = await _dag.ApplyMutationAsync(dagId, mutation, ct);
        var acceptedNodes = mutation.UpsertNodes
            .Select(n => new Dictionary<string, object?>
            {
                ["id"] = n.Id ?? string.Empty,
                ["type"] = n.Type.ToString()
            })
            .ToList();

        session.Events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "aevatar.vibe.dag_updated",
            Value = new
            {
                sessionId = session.Id,
                dagId,
                runId = ResolveRunId(coordinator),
                mutationId = mutation.MutationId,
                nodes = mutation.UpsertNodes.Count,
                edges = mutation.UpsertEdges.Count,
                consensusWorkflow = "workflow",
                consensusArtifact = string.Empty,
                updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? string.Empty
            }
        });

        _logger.LogInformation("DAG applied: {DagId} nodes={Nodes} edges={Edges}",
            dagId, mutation.UpsertNodes.Count, mutation.UpsertEdges.Count);

        return PrimitiveResult.Ok(new Dictionary<string, object>
        {
            ["accepted"] = true,
            ["blocked"] = false,
            ["parse_failed"] = false,
            ["red_flags"] = redFlags,
            ["mutation_id"] = mutation.MutationId ?? string.Empty,
            ["nodes"] = mutation.UpsertNodes.Count,
            ["edges"] = mutation.UpsertEdges.Count,
            ["applied"] = true,
            ["updated_at"] = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? string.Empty,
            ["accepted_nodes"] = acceptedNodes
        });
    }

    private static (bool Accepted, List<string> RedFlags) ResolveConsensus(IWorkflowCoordinatorRuntime coordinator, string consensusKey)
    {
        if (!coordinator.TryGetWorkflowVariable(consensusKey, out var obj) || obj == null)
            return (false, []);

        if (obj is Dictionary<string, object?> dict)
            return ParseConsensusDict(dict);

        if (obj is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject())
                    map[p.Name] = p.Value;
                return ParseConsensusDict(map);
            }
        }

        if (obj is string raw && TryExtractJson(raw, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null)
                    return ParseConsensusDict(parsed);
            }
            catch
            {
                return (false, []);
            }
        }

        return (false, []);
    }

    private static (bool Accepted, List<string> RedFlags) ParseConsensusDict(Dictionary<string, object?> dict)
    {
        var accepted = false;
        if (dict.TryGetValue("accept", out var acceptObj) && acceptObj != null)
        {
            accepted = acceptObj switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                JsonElement el when el.ValueKind == JsonValueKind.True => true,
                JsonElement el when el.ValueKind == JsonValueKind.False => false,
                _ => false
            };
        }

        var redFlags = new List<string>();
        if (dict.TryGetValue("red_flags", out var redFlagsObj) && redFlagsObj != null)
        {
            redFlags.AddRange(NormalizeRedFlags(redFlagsObj));
        }

        return (accepted, redFlags);
    }

    private static IEnumerable<string> NormalizeRedFlags(object value)
    {
        switch (value)
        {
            case List<string> list:
                return list.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim());
            case IEnumerable<object> objs:
                return objs.Select(x => x?.ToString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim());
            case JsonElement el when el.ValueKind == JsonValueKind.Array:
            {
                var list = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        list.Add(text!.Trim());
                }
                return list;
            }
            case string single when !string.IsNullOrWhiteSpace(single):
                return [single.Trim()];
            default:
                return [];
        }
    }
}
