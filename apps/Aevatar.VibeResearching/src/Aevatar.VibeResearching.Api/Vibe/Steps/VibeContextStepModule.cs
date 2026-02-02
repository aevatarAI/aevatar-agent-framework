using System.Text;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeContextStepModule : VibeStepModuleBase
{
    private readonly MaterialsService _materials;
    private readonly DagStore _dag;
    private readonly PaperService _paper;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibeContextStepModule> _logger;

    public VibeContextStepModule(
        MaterialsService materials,
        DagStore dag,
        PaperService paper,
        ResearchSessionManager sessions,
        ILogger<VibeContextStepModule> logger)
    {
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_context";
    public override string StepType => "vibe_context";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_context requires session_id");

        var question = ResolveQuestion(coordinator);
        var outlineChars = ResolveIntVar(coordinator, "outline_excerpt_chars", 8000);
        var draftChars = ResolveIntVar(coordinator, "draft_excerpt_chars", 12000);

        var session = _sessions.GetOrCreate(sessionId);
        var dagId = session.EffectiveDagId;

        SraDagSnapshot dag;
        try
        {
            dag = await _dag.LoadSnapshotAsync(dagId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load DAG snapshot (best-effort).");
            dag = new SraDagSnapshot { SessionId = dagId };
        }

        var planContext = BuildPlanContextFromDag(dag);

        string materialsContext;
        try
        {
            var materials = await _materials.LoadAsync(sessionId, dagId, question, ct);
            materialsContext = materials.RenderedContext ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load materials (best-effort).");
            materialsContext = string.Empty;
        }

        var ws = await _paper.EnsurePaperFilesAsync(sessionId, ct);
        var outlineExcerpt = await ReadExcerptAsync(ws.PaperOutlinePath, outlineChars, ct);
        var draftExcerpt = await ReadExcerptAsync(ws.PaperDraftPath, draftChars, ct);

        var payload = new Dictionary<string, object>
        {
            ["plan_context"] = planContext,
            ["dag_nodes"] = dag.Nodes.Count,
            ["dag_edges"] = dag.Edges.Count,
            ["materials_context"] = materialsContext,
            ["outline_excerpt"] = outlineExcerpt,
            ["draft_excerpt"] = draftExcerpt
        };

        return PrimitiveResult.Ok(payload);
    }

    private static async Task<string> ReadExcerptAsync(string path, int maxChars, CancellationToken ct)
    {
        if (maxChars <= 0) return string.Empty;
        if (!File.Exists(path)) return string.Empty;

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        text = (text ?? string.Empty).Replace("\r", "").Trim();
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static string BuildPlanContextFromDag(SraDagSnapshot dag)
    {
        dag ??= new SraDagSnapshot();

        static int SafeInt(string? s)
            => int.TryParse((s ?? string.Empty).Trim(), out var x) ? x : 0;

        bool IsMilestone(SraDagNode n) =>
            n.Tags != null &&
            n.Tags.TryGetValue("planKind", out var v) &&
            string.Equals((v ?? string.Empty).Trim(), "milestone", StringComparison.OrdinalIgnoreCase);

        bool IsRoundPlan(SraDagNode n) =>
            n.Tags != null &&
            n.Tags.TryGetValue("planKind", out var v) &&
            string.Equals((v ?? string.Empty).Trim(), "round", StringComparison.OrdinalIgnoreCase);

        var allPlans = dag.Nodes
            .Where(n => n != null && n.Kind == SraDagNodeKind.Plan)
            .ToList();

        var milestones = allPlans
            .Where(n => IsMilestone(n!))
            .OrderBy(n =>
            {
                n!.Tags.TryGetValue("milestoneRoundIndex", out var s);
                var x = SafeInt(s);
                return x <= 0 ? int.MaxValue : x;
            })
            .ThenBy(n => n!.Id, StringComparer.Ordinal)
            .Take(8)
            .ToList();

        var roundPlan = allPlans
            .Where(n => IsRoundPlan(n!))
            .OrderByDescending(n => n!.UpdatedAt?.ToDateTime().ToUniversalTime() ?? DateTime.MinValue)
            .ThenByDescending(n => n!.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var plans = new List<SraDagNode>(capacity: 12);
        plans.AddRange(milestones!);
        if (roundPlan != null) plans.Add(roundPlan);

        if (plans.Count == 0)
            return "(no plan nodes yet)";

        var sb = new StringBuilder(512);
        foreach (var p in plans)
        {
            var id = (p.Id ?? string.Empty).Trim();
            var label = Bound((p.Label ?? string.Empty).Replace("\r", "").Trim(), 220);
            sb.Append("- ").Append(id.Length == 0 ? "plan" : id).Append(": ").Append(label).AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string Bound(string s, int max)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= max) return t;
        return t[..max];
    }
}
