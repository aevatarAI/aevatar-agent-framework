using Aevatar.Agents.Cognitive.Primitives;

namespace Aevatar.Agents.Cognitive.Execution.Run;

public sealed record MeshExecutionRequest(
    WorkflowRunContext Context,
    StepDefinition Step);

public sealed record MeshExecutionResult(
    bool Ok,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<string> Errors);

public interface IMeshExecutionRunner
{
    Task<MeshExecutionResult> ExecuteAsync(MeshExecutionRequest request, CancellationToken ct);
}

public sealed class MeshExecutionStepModule : WorkflowRunStepModuleBase
{
    private readonly IMeshExecutionRunner _runner;

    public MeshExecutionStepModule(IMeshExecutionRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public override string Name => "mesh_execution";
    public override string StepType => "mesh_execute";

    public override async Task<PrimitiveResult> ExecuteAsync(
        WorkflowRunContext context,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var result = await _runner.ExecuteAsync(new MeshExecutionRequest(context, step), ct);
        if (result.Ok)
        {
            return PrimitiveResult.Ok(result.Outputs);
        }

        var error = result.Errors.Count == 0 ? "mesh execution failed" : string.Join(" | ", result.Errors);
        return new PrimitiveResult
        {
            Success = false,
            Error = error,
            Value = result.Outputs
        };
    }
}
