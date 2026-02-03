using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.AGUI;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Vibe.Dag;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibePlanApplyStepModule : VibeStepModuleBase
{
    private readonly DagStore _dag;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibePlanApplyStepModule> _logger;

    public VibePlanApplyStepModule(
        DagStore dag,
        ResearchSessionManager sessions,
        ILogger<VibePlanApplyStepModule> logger)
    {
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_plan_apply";
    public override string StepType => "vibe_plan_apply";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_plan_apply requires session_id");

        var planVar = ResolveStringParameter(step.Parameters, "plan_var", "plan_json");
        var planTextVar = ResolveStringParameter(step.Parameters, "plan_text_var", "planner");
        var source = ResolveStringParameter(step.Parameters, "source", "planner");
        var author = ResolveStringParameter(step.Parameters, "author", "planner");
        var maxMilestones = ResolveIntParameter(step.Parameters, "max_milestones", 12);
        maxMilestones = Math.Clamp(maxMilestones, 1, 30);

        var planValue = ResolveWorkflowValue(coordinator, planVar);
        var planText = ResolveStringVar(coordinator, planTextVar);
        if (!TryParsePlan(planValue, maxMilestones, out var items, out var note, out var rawPlan))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["applied"] = false,
                ["parse_failed"] = true,
                ["reason"] = $"invalid plan_var: {planVar}"
            });
        }

        if (items.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["applied"] = false,
                ["parse_failed"] = false,
                ["reason"] = "no milestones"
            });
        }

        var session = _sessions.GetOrCreate(sessionId);
        var dagId = session.EffectiveDagId;

        try
        {
            var currentDag = await _dag.LoadSnapshotAsync(dagId, ct);
            var proofSource = string.IsNullOrWhiteSpace(planText) ? rawPlan : planText;
            var mutation = PlanDagMutationBuilder.BuildMilestoneMutation(
                sessionId: session.Id,
                author: author,
                source: source,
                items: items,
                currentDag: currentDag,
                proofHeader: "PlannerOutput",
                proofSource: proofSource,
                mutationIdPrefix: "plan",
                markRemoved: true);

            if (mutation.UpsertNodes.Count == 0 && mutation.UpsertEdges.Count == 0)
            {
                return PrimitiveResult.Ok(new Dictionary<string, object>
                {
                    ["applied"] = false,
                    ["parse_failed"] = false,
                    ["reason"] = "empty mutation"
                });
            }

            var applied = await _dag.ApplyMutationAsync(dagId, mutation, ct);

            session.Events.Publish(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.vibe.dag_updated",
                Value = new
                {
                    sessionId = session.Id,
                    dagId,
                    runId = ResolveRunId(coordinator),
                    mutationId = mutation.MutationId ?? string.Empty,
                    nodes = mutation.UpsertNodes.Count,
                    edges = mutation.UpsertEdges.Count,
                    source,
                    updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? string.Empty
                }
            });

            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["applied"] = true,
                ["parse_failed"] = false,
                ["mutation_id"] = mutation.MutationId ?? string.Empty,
                ["nodes"] = mutation.UpsertNodes.Count,
                ["edges"] = mutation.UpsertEdges.Count,
                ["note"] = note ?? string.Empty,
                ["updated_at"] = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            _logger.LogWarning(ex, "vibe_plan_apply failed.");
            return PrimitiveResult.Ok(new Dictionary<string, object>
            {
                ["applied"] = false,
                ["parse_failed"] = false,
                ["reason"] = ex.Message
            });
        }
    }

    private static object? ResolveWorkflowValue(IWorkflowCoordinatorRuntime coordinator, string key)
        => coordinator.TryGetWorkflowVariable(key, out var value) ? value : null;

    private static bool TryParsePlan(
        object? value,
        int maxMilestones,
        out List<PlanDagMutationBuilder.PlanMilestoneItem> items,
        out string? note,
        out string rawPlan)
    {
        items = [];
        note = null;
        rawPlan = string.Empty;

        if (value == null)
            return false;

        string? json = null;
        switch (value)
        {
            case JsonElement el:
                json = el.GetRawText();
                rawPlan = json ?? string.Empty;
                break;
            case Dictionary<string, object?> dict:
                json = JsonSerializer.Serialize(dict);
                rawPlan = json;
                break;
            case string text:
                rawPlan = text;
                if (!TryExtractJson(text, out json))
                    json = text;
                break;
            default:
                rawPlan = value.ToString() ?? string.Empty;
                if (!TryExtractJson(rawPlan, out json))
                    json = rawPlan;
                break;
        }

        if (string.IsNullOrWhiteSpace(json))
            return false;

        PlanJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlanJson>(json!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return false;
        }

        note = parsed?.Note?.Trim();
        var list = parsed?.Milestones ?? [];
        foreach (var m in list)
        {
            var expected = (m?.ExpectedOutput ?? string.Empty).Replace("\r", "").Trim();
            if (expected.Length == 0) continue;
            var round = Math.Clamp(m?.RoundIndex ?? 0, 0, 200);
            items.Add(new PlanDagMutationBuilder.PlanMilestoneItem(round, expected));
            if (items.Count >= maxMilestones) break;
        }

        return true;
    }

    private static int ResolveIntParameter(Dictionary<string, object?> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return fallback;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private sealed class PlanJson
    {
        public List<PlanMilestoneJson>? Milestones { get; init; }
        public string? Note { get; init; }
    }

    private sealed class PlanMilestoneJson
    {
        public int? RoundIndex { get; init; }
        public string? ExpectedOutput { get; init; }
    }
}
