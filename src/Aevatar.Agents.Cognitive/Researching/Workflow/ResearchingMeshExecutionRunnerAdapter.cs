using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Round;
using Aevatar.Agents.Cognitive.Researching.Workflow;

namespace Aevatar.Agents.Cognitive.Researching.Workflow;

public sealed class ResearchingMeshExecutionRunnerAdapter : IMeshExecutionRunner
{
    private readonly ResearchingRoundServices _services;

    public ResearchingMeshExecutionRunnerAdapter(ResearchingRoundServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public async Task<MeshExecutionResult> ExecuteAsync(MeshExecutionRequest request, CancellationToken ct)
    {
        var context = request.Context;
        var session = context.GetSession();
        var materials = context.TryGetMaterials() ?? new MaterialsSnapshot();
        var resolver = await _services.GetProviderResolverAsync(context, context.GetProviderOverride(), ct);

        var ctx = _services.BuildRoundContext(context, materials);
        var outputs = await _services.RunWorkerPhaseAsync(
            context,
            ctx,
            session.EffectiveDagId,
            resolver,
            ct);

        context.SetMeshOutputs(outputs);

        var errors = new List<string>();
        if (outputs.TryGetValue("mesh_error", out var error) && !string.IsNullOrWhiteSpace(error))
            errors.Add(error);
        if (outputs.TryGetValue("mesh_error_kind", out var kind) && !string.IsNullOrWhiteSpace(kind))
            errors.Add(kind);

        return new MeshExecutionResult(errors.Count == 0, outputs, errors);
    }
}
