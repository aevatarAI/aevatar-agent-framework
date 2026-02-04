using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Round;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Trace;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Workflow;

internal static class ResearchingRunContextKeys
{
    public const string Session = "researching.session";
    public const string Input = "researching.input";
    public const string Question = "researching.question";
    public const string ProviderOverride = "researching.provider_override";
    public const string Materials = "researching.materials";
    public const string DagSnapshot = "researching.dag_snapshot";
    public const string RecentTrace = "researching.recent_trace";
    public const string MeshOutputs = "researching.mesh_outputs";
    public const string DagResult = "researching.dag_result";
}

internal static class ResearchingRunContextExtensions
{
    public static ResearchSession GetSession(this WorkflowRunContext context)
        => GetRequired<ResearchSession>(context, ResearchingRunContextKeys.Session);

    public static ResearchingInput GetInput(this WorkflowRunContext context)
        => GetRequired<ResearchingInput>(context, ResearchingRunContextKeys.Input);

    public static string GetQuestion(this WorkflowRunContext context)
    {
        var question = GetRequired<string>(context, ResearchingRunContextKeys.Question);
        return question.Trim();
    }

    public static string? GetProviderOverride(this WorkflowRunContext context)
        => GetOptional<string>(context, ResearchingRunContextKeys.ProviderOverride);

    public static MaterialsSnapshot? TryGetMaterials(this WorkflowRunContext context)
        => GetOptional<MaterialsSnapshot>(context, ResearchingRunContextKeys.Materials);

    public static void SetMaterials(this WorkflowRunContext context, MaterialsSnapshot snapshot)
        => context.Items[ResearchingRunContextKeys.Materials] = snapshot;

    public static SraDagSnapshot? TryGetDagSnapshot(this WorkflowRunContext context)
        => GetOptional<SraDagSnapshot>(context, ResearchingRunContextKeys.DagSnapshot);

    public static void SetDagSnapshot(this WorkflowRunContext context, SraDagSnapshot snapshot)
        => context.Items[ResearchingRunContextKeys.DagSnapshot] = snapshot;

    public static IReadOnlyList<SraRoundSummary> GetRecentTrace(this WorkflowRunContext context)
    {
        var trace = GetOptional<IReadOnlyList<SraRoundSummary>>(context, ResearchingRunContextKeys.RecentTrace);
        return trace ?? Array.Empty<SraRoundSummary>();
    }

    public static void SetRecentTrace(this WorkflowRunContext context, IReadOnlyList<SraRoundSummary> trace)
        => context.Items[ResearchingRunContextKeys.RecentTrace] = trace;

    public static IReadOnlyDictionary<string, string> GetMeshOutputs(this WorkflowRunContext context)
    {
        var outputs = GetOptional<IReadOnlyDictionary<string, string>>(context, ResearchingRunContextKeys.MeshOutputs);
        return outputs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static void SetMeshOutputs(this WorkflowRunContext context, IReadOnlyDictionary<string, string> outputs)
        => context.Items[ResearchingRunContextKeys.MeshOutputs] = outputs;

    public static DagRoundResult? TryGetDagResult(this WorkflowRunContext context)
        => GetOptional<DagRoundResult>(context, ResearchingRunContextKeys.DagResult);

    public static void SetDagResult(this WorkflowRunContext context, DagRoundResult result)
        => context.Items[ResearchingRunContextKeys.DagResult] = result;

    private static T GetRequired<T>(WorkflowRunContext context, string key)
    {
        if (context.Items.TryGetValue(key, out var obj) && obj is T typed)
            return typed;
        throw new InvalidOperationException($"Missing workflow context item: {key}");
    }

    private static T? GetOptional<T>(WorkflowRunContext context, string key)
    {
        if (context.Items.TryGetValue(key, out var obj) && obj is T typed)
            return typed;
        return default;
    }
}
